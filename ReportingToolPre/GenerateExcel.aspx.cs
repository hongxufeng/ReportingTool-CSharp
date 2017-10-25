using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.IO;

namespace IMSROOT.ReportingToolPre
{
    public partial class GenerateExcel : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            MemoryStream excelStream = new MemoryStream();

            try
            {
                DataHelper.getTable(HttpContext.Current, excelStream, "exportExcel");
                var binaryStream = excelStream.ToArray();

                //Response.ContentType = "application/vnd.ms-excel";
                Response.AddHeader("Content-Disposition", string.Format("attachment;filename={0}", Request["table"] + ".xlsx"));
                Response.AddHeader("Content-Length", binaryStream.Length.ToString());
                Response.Clear();
                Response.BinaryWrite(binaryStream);
                Response.End();
            }
            catch (Exception exception)
            {

            }
            finally
            {
                excelStream.Close();
            }
        }
    }
}