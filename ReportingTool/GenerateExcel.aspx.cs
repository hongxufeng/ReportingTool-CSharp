using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace IMSROOT.ReportingTool
{
    public partial class GenerateExcel : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            MemoryStream excelStream = new MemoryStream();

            try
            {
                DataHelper.GetTable(HttpContext.Current, excelStream, "ExportExcel");
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