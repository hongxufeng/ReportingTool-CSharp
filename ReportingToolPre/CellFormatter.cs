using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace IMSROOT.ReportingToolPre
{
    public class CellFormatter
    {
        public static string PutInText(KeyValuePair<string, string> currentCell, Dictionary<string, string> colsDict)
        {
            return "<input type=\\\"text\\\" value=\\\"" + currentCell.Value + "\\\">";
        }
    }
}