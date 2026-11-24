using System.Data;
using Microsoft.Data.SqlClient;
using ToolAleart_Inspection.Model;

namespace ToolAleart_Inspection.Database
{
    public class ISql
    {
        private static string sqlConnectKVH = @"Data Source=192.168.122.2;Initial Catalog=KVH;User ID=kvh_write;Password=kvh_wr0te@;";

        // Microsoft.Data.SqlClient mặc định Encrypt=true → SQL Server nội bộ (cert tự ký) sẽ bị từ chối.
        private static readonly string connectionString =
            new SqlConnectionStringBuilder(sqlConnectKVH)
            {
                TrustServerCertificate = true,
                ConnectTimeout = 10
            }.ConnectionString;

        // Lấy trạng thái crack mới nhất của 1 máy
        private const string queryGetData = @"
            SELECT TOP 1 ID, CodeMachine, Po, isCrack, DateCreated
            FROM KVH.dbo.CheckCrackPr
            WHERE CodeMachine = @codeMachine
            ORDER BY DateCreated DESC, ID DESC;";

        // Trùng cả CodeMachine + Po thì cập nhật isCrack + DateCreated, còn lại INSERT dòng mới
        // (lock để 2 lần ghi cùng lúc không tạo trùng)
        private const string queryUpsert = @"
            SET XACT_ABORT ON;
            BEGIN TRAN;

            UPDATE KVH.dbo.CheckCrackPr WITH (UPDLOCK, HOLDLOCK)
            SET    isCrack = @isCrack,
                   DateCreated = GETDATE()
            WHERE  CodeMachine = @codeMachine
              AND  Po = @po;

            IF @@ROWCOUNT = 0
                INSERT INTO KVH.dbo.CheckCrackPr (CodeMachine, Po, isCrack)
                VALUES (@codeMachine, @po, @isCrack);

            COMMIT;";

        // PC → master mới nhất → PO mới nhất (OEPDT, giống QAToolcheckCamera) của máy → trạng thái crack của đúng máy + PO đó
        private const string queryCrackStatus = @"
            SELECT  cm.pc_name,
                    cm.process_name,
                    ins.ID AS InspectionId,
                    ins.SRGNO,
                    ins.OEPDT,
                    cr.isCrack,
                    cr.DateCreated
            FROM (
                    SELECT TOP 1 pc_name, process_name
                    FROM F2Database.dbo.F2_Camera_Master
                    WHERE pc_name = @pcName
                    ORDER BY reg_date DESC
                 ) cm
            OUTER APPLY (
                    SELECT TOP 1 i.ID, i.SRGNO, i.OEPDT
                    FROM F2Database.dbo.F2InspectionSystemData i
                    WHERE i.MACNM = cm.process_name
                    ORDER BY i.OEPDT DESC, i.ID DESC
                 ) ins
            OUTER APPLY (
                    SELECT TOP 1 c.isCrack, c.DateCreated
                    FROM KVH.dbo.CheckCrackPr c
                    WHERE c.CodeMachine = cm.process_name
                      AND c.Po = ins.SRGNO
                    ORDER BY c.DateCreated DESC, c.ID DESC
                    ) cr;";

        private const string queryCheckPrCrack = @"
            SELECT CAST(
                     CASE WHEN EXISTS (
                            SELECT 1
                            FROM MANUFASPCPD.dbo.MANUFA_F_PD_DT_REQ_DTL1 DTL1
                            WHERE DTL1.AUFNR = @po
                              AND DTL1.AFVC_LTXA1 LIKE '%Crack Check%'
                          )
                          THEN 1 ELSE 0
                     END AS BIT) AS HasCrackCheck;";


        /// <summary>
        /// Kiểm tra PO hiện tại có công đoạn crack không .
        /// </summary>
        /// 
        public static async Task<bool> CheckprocessCrack(string po, CancellationToken ct = default)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(queryCheckPrCrack, conn) { CommandTimeout = 15 };
            cmd.Parameters.Add("@po", SqlDbType.VarChar, 100).Value = po;

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return false;

            return reader["HasCrackCheck"] == DBNull.Value ? false : ToBool(reader["HasCrackCheck"]);
        }

        /// <summary>
        /// Kiểm tra PC đang chạy PO nào và PO đó có bị crack không.
        /// Trả về null nếu PC chưa có trong F2_Camera_Master.
        /// Lỗi kết nối/SQL được ném ra để nơi gọi xử lý.
        /// </summary>
        public static async Task<CrackStatus?> GetCrackStatusAsync(string pcName, CancellationToken ct = default)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            using var cmd = new SqlCommand(queryCrackStatus, conn) { CommandTimeout = 15 };
            cmd.Parameters.Add("@pcName", SqlDbType.VarChar, 100).Value = pcName;

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new CrackStatus
            {
                PcName = reader["pc_name"]?.ToString() ?? pcName,
                ProcessName = reader["process_name"]?.ToString() ?? string.Empty,
                InspectionId = reader["InspectionId"] == DBNull.Value ? null : Convert.ToInt64(reader["InspectionId"]),
                Po = reader["SRGNO"] == DBNull.Value ? null : reader["SRGNO"].ToString(),
                InspectionDate = ToDateTime(reader["OEPDT"]),
                IsCrack = reader["isCrack"] == DBNull.Value ? null : ToBool(reader["isCrack"]),
                CrackCheckedAt = ToDateTime(reader["DateCreated"])
            };
        }

        /// <summary>
        /// Lấy dữ liệu crack của máy. Trả về null nếu máy chưa có dòng nào.
        /// Lỗi kết nối/SQL sẽ được ném ra để nơi gọi tự xử lý (log, hiển thị...).
        /// </summary>
        public static async Task<DataCrack?> GetDataCrackAsync(string codeMachine)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(queryGetData, conn);
            cmd.Parameters.Add("@codeMachine", SqlDbType.VarChar, 100).Value = codeMachine;

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new DataCrack
            {
                ID = Convert.ToInt32(reader["ID"]),
                CodeMachine = reader["CodeMachine"] as string ?? string.Empty,
                Po = reader["Po"] as string ?? string.Empty,
                isCrack = ToBool(reader["isCrack"]),
                DateCreated = ToDateTime(reader["DateCreated"])
            };
        }

        /// <summary>
        /// Ghi trạng thái crack: nếu đã có dòng trùng CodeMachine + Po thì chỉ cập nhật DateCreated,
        /// ngược lại thêm dòng mới (isCrack chỉ được ghi khi thêm mới).
        /// </summary>
        public static async Task UpsertDataCrackAsync(string codeMachine, string po, bool isCrack)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(queryUpsert, conn);
            cmd.Parameters.Add("@codeMachine", SqlDbType.VarChar, 100).Value = codeMachine;
            cmd.Parameters.Add("@po", SqlDbType.VarChar, 100).Value = po ?? string.Empty; // NULL không so khớp được bằng '='
            cmd.Parameters.Add("@isCrack", SqlDbType.Bit).Value = isCrack;

            await cmd.ExecuteNonQueryAsync();
        }

        // BIT → bool; chịu được cả trường hợp cột lưu 0/1 dạng số hoặc chuỗi
        private static bool ToBool(object value) => value switch
        {
            bool b => b,
            byte or short or int or long => Convert.ToInt64(value) != 0,
            string s => s.Trim() == "1" || (bool.TryParse(s, out var r) && r),
            _ => false // NULL
        };

        // DATETIME → DateTime; nếu cột vẫn là VARCHAR thì thử parse
        private static DateTime? ToDateTime(object value) => value switch
        {
            DateTime d => d,
            string s when DateTime.TryParse(s, out var p) => p,
            _ => null
        };
    }
}
