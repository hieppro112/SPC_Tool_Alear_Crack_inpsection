namespace ToolAleart_Inspection.Model
{
    /// <summary>
    /// Trạng thái crack hiện tại của 1 PC: đang chạy PO nào và PO đó có bị crack không.
    /// </summary>
    public class CrackStatus
    {
        public string PcName { get; set; } = string.Empty;

        /// <summary>Tên máy/công đoạn (F2_Camera_Master.process_name = MACNM = CodeMachine).</summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>ID dòng Inspection mới nhất; null nếu máy chưa có dữ liệu.</summary>
        public long? InspectionId { get; set; }

        /// <summary>PO đang chạy (SRGNO); null nếu máy chưa có dữ liệu.</summary>
        public string? Po { get; set; }

        public DateTime? InspectionDate { get; set; }

        /// <summary>true = crack, false = OK, null = PO chưa được QAToolcheckCamera kiểm tra video.</summary>
        public bool? IsCrack { get; set; }

        public DateTime? CrackCheckedAt { get; set; }
    }
}
