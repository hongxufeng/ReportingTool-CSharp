using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using LinqToSql.Orm;

namespace IMSROOT.ReportingTool
{
    public class CellFormatter
    {
        public static string PutInText(KeyValuePair<string, string> currentCell, Dictionary<string, string> colsDict)
        {
            return "<input type=\\\"text\\\" class=\\\"rt-celltext\\\" value=\\\"" + currentCell.Value + "\\\">";
        }

        public static string XXList(KeyValuePair<string, string> currentCell, Dictionary<string, string> colsDict)
        {
            List<string> list = new List<string> { "领料出库", "物资借出" };

            StringBuilder sb = new StringBuilder();
            sb.Append("<select class=\\\"rt-cellselect\\\">");

            foreach (var i in list)
            {
                sb.Append("<option value=\\\"" + i + "\\\"");

                if (i == currentCell.Value)
                {
                    sb.Append(" selected=\\\"selected\\\"");
                }

                sb.Append(">" + i + "</option>");
            }

            return sb.ToString();
        }
        public static string FeedbackGetNumber(KeyValuePair<string, string> currentCell, Dictionary<string, string> colsDict)
        {
            StringBuilder sb = new StringBuilder();
            if (currentCell.Value.Length != 0)
            {
                int objid = Convert.ToInt32(currentCell.Value);
                sb.Append("<b>参与人数</b>:");
                DataClassesDataContext dc = new DataClassesDataContext();
                var answerList = from answer in dc.T_imspaperanswers where answer.roleA_id == objid select answer;
                if (answerList.Any())
                {
                    sb.Append(answerList.Count().ToString());
                }
                else
                {
                    sb.Append("0");
                }
            }
            else
            {
                return string.Empty;
            }
            return sb.ToString();
        }
    }
}