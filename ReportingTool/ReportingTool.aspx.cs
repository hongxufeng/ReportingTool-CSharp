using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace IMSROOT.ReportingTool
{
    public partial class ReportingTool : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (Session["identitystate"] == null)
            {
                Response.Write("<script>alert('对不起，您的权限不足或登录状态已失效'); location.href = '../../index.aspx';</script>");
            }
        }
    }
}