using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.SessionState;
using System.Xml.Linq;

namespace IMSROOT.ReportingTool
{
    /// <summary>
    /// Summary description for Handler
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
            string cmd = context.Request.Form["cmd"];
            string response = string.Empty;

            try
            {
                switch (cmd)
                {
                    case "GetTable":
                        response = DataHelper.GetTable(context, ms);
                        break;
                    case "SearchTree":
                        response = DataHelper.SearchTree(context);
                        break;
                    case "LocateNode":
                        response = DataHelper.LocateNode(context);
                        break;
                    default:
                        response = RequestStatus.exception("DefaultCase");
                        break;
                }
            }
            catch (Exception exception)
            {
                response = RequestStatus.exception(exception.Message);
            }
            finally
            {
                ms.Close();
                context.Response.Write(response);
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