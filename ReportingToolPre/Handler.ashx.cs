using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.IO;
using System.Web.SessionState;

namespace IMSROOT.ReportingToolPre
{
    /// <summary>
    /// Handler 的摘要说明
    /// </summary>
    public class Handler : IHttpHandler, IRequiresSessionState
    {

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "text/plain";

            if (context.Session["identitystate"] == null)
            {
                context.Response.Write(RequestStatus.insufficientPrivileges());
                context.ApplicationInstance.CompleteRequest();
                return;
            }

            MemoryStream ms = new MemoryStream();

            try
            {
                switch (context.Request.Form["cmd"])
                {
                    case "getTab": context.Response.Write(DataHelper.getTab(context, ms));
                        break;
                    default: context.Response.Write(DataHelper.getTable(context, ms));
                        break;
                };
            }
            catch (Exception exception)
            {
                context.Response.Write(RequestStatus.exception(exception.Message));
            }
            finally
            {
                ms.Close();
            }
        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }
    }
}