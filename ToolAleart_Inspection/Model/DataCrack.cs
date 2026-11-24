using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ToolAleart_Inspection.Model
{
    public class DataCrack
    {
        public int ID { get; set; }
        public string CodeMachine { get; set; } = string.Empty;
        public string Po { get; set; } = string.Empty;
        public bool isCrack { get; set; }
        public DateTime? DateCreated { get; set; }
    }
}
