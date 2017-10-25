using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.SessionState;
using System.Xml.Linq;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using LinqToSql.Orm;

namespace IMSROOT.ReportingToolPre
{
    public class DataHelper : IRequiresSessionState
    {
        static readonly string defaultPasswordHash = "CAXACAXA";
        static readonly string defaultSaltKey = "CAXACAXA";
        static readonly string defaultVIKey = "CAXACAXACAXACAXA";

        public static string getTab(HttpContext context, MemoryStream excelStream)
        {
            string tableID = context.Request["table"];
            XDocument xml = XDocument.Load(context.Request.PhysicalApplicationPath + "ReportingTool\\xml\\" + context.Request["ConfigFile"] + ".xml");
            XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == tableID select n).FirstOrDefault();

            if (xmlTable == null)
            {
                Exception exception = new Exception("数据表配置\"" + tableID + "\"不存在");
                throw exception;
            }

            string table = string.Empty;

            if (isAdministrator(context, xmlTable) && xmlTable.Attribute("adminname") != null)
            {
                table = xmlTable.Attribute("adminname").Value;
            }
            else
            {
                table = xmlTable.Attribute("name").Value;
            }

            string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["PLMConnectionString"].ConnectionString;
            SqlConnection sqlCon = new SqlConnection(connectionString);
            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.Connection = sqlCon;

            if (xmlTable.Attribute("exec-before") != null)
            {
                executeStoredProcedure(context.Request, xmlTable, sqlCon, sqlCmd);
            }

            SqlDataAdapter dataAdapter = new SqlDataAdapter("SELECT * FROM " + table, connectionString);


            Dictionary<string, string> paramDict = new Dictionary<string, string>();
            string[] allKeys = context.Request.QueryString.AllKeys;
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (table.Contains("@" + allKeys[i]))
                {
                    dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                    {
                        ParameterName = "@" + allKeys[i],
                        Value = context.Request.QueryString[allKeys[i]],
                    });
                    paramDict.Add("@" + allKeys[i], context.Request.QueryString[allKeys[i]]);
                }
            }

            DataTable dataTable = new DataTable();
            dataAdapter.FillSchema(dataTable, SchemaType.Source);
            dataAdapter.Fill(dataTable);
            StringBuilder tabBuilder = new StringBuilder();
            if (xmlTable.Attribute("tabby") != null)
            {
                appendTab(xmlTable, dataTable, tabBuilder);
            }
            return "{\"tab\":\"" + tabBuilder.ToString() + "\"}";
        }
        public static string getTable(HttpContext context, MemoryStream excelStream, string cmd = "getTable")
        {
            string tableID = context.Request["table"];
            XDocument xml = XDocument.Load(context.Request.PhysicalApplicationPath + "ReportingTool\\xml\\" + context.Request["ConfigFile"] + ".xml");
            XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == tableID select n).FirstOrDefault();

            if (xmlTable == null)
            {
                Exception exception = new Exception("数据表配置\"" + tableID + "\"不存在");
                throw exception;
            }

            string table = string.Empty;

            if (isAdministrator(context, xmlTable) && xmlTable.Attribute("adminname") != null)
            {
                table = xmlTable.Attribute("adminname").Value;
            }
            else
            {
                table = xmlTable.Attribute("name").Value;
            }

            string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["PLMConnectionString"].ConnectionString;
            SqlConnection sqlCon = new SqlConnection(connectionString);
            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.Connection = sqlCon;

            if (xmlTable.Attribute("exec-before") != null)
            {
                executeStoredProcedure(context.Request, xmlTable, sqlCon, sqlCmd);
            }

            SqlDataAdapter dataAdapter = new SqlDataAdapter("SELECT * FROM " + table, connectionString);


            Dictionary<string, string> paramDict = new Dictionary<string, string>();
            string[] allKeys = context.Request.QueryString.AllKeys;
            for (int i = 0; i < allKeys.Length; i++)
            {
                if (table.Contains("@" + allKeys[i]))
                {
                    dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                    {
                        ParameterName = "@" + allKeys[i],
                        Value = context.Request.QueryString[allKeys[i]],
                    });
                    paramDict.Add("@" + allKeys[i], context.Request.QueryString[allKeys[i]]);
                }
            }

            DataTable dataTable = new DataTable();
            dataAdapter.FillSchema(dataTable, SchemaType.Source);

            XElement firstColumn = xmlTable.Descendants().FirstOrDefault();
            if (firstColumn == null)
            {
                Exception exception = new Exception("数据表配置中至少要包含一列");
                throw exception;
            }
            else if (xmlTable.Nodes().Count() == 1 && (firstColumn.Name == "pagerbuttons" || firstColumn.Name == "buttons"))
            {
                Exception exception = new Exception("数据表配置中至少要包含一列");
                throw exception;
            }

            int page = Convert.ToInt32(context.Request.Form["page"]);
            int rows = Convert.ToInt32(context.Request.Form["rows"]);
            string orderBy = context.Request["orderby"];
            string xsc = context.Request["xsc"];

            StringBuilder sqlBuilder = new StringBuilder();

            string order = appendOrderBy(xmlTable, dataTable, orderBy, xsc);
            if (order == string.Empty)
            {
                order = "ORDER BY " + firstColumn.Name;
            }

            sqlBuilder.Append("FROM (");
            sqlBuilder.Append("SELECT ROW_NUMBER() OVER(" + order + ") AS RowNumber, * FROM " + table);
            appendWhere(context, xmlTable, dataTable, sqlBuilder, paramDict);

            sqlBuilder.Append(") AS gridTable");

            int totalRecords = 0;
            int totalPages = 0;

            if (cmd != "exportExcel")
            {
                countRecords(sqlCon, sqlCmd, sqlBuilder, paramDict, rows, ref totalRecords, ref totalPages);
            }

            if (cmd == "exportExcel")
            {
                exportExcel(xmlTable, dataTable, dataAdapter, sqlBuilder, paramDict, excelStream);
                return null;
            }

            sqlBuilder.Append(" WHERE RowNumber BETWEEN " + ((page - 1) * rows + 1) + " AND " + page * rows);

            dataAdapter.SelectCommand.CommandText = "SELECT * " + sqlBuilder.ToString();
            dataAdapter.SelectCommand.Parameters.Clear();
            foreach (var pair in paramDict)
            {
                dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                {
                    ParameterName = pair.Key,
                    Value = pair.Value,
                });
            }

            dataTable.PrimaryKey = null;
            dataAdapter.Fill(dataTable);
            dataTable.Columns.Remove("RowNumber");

            int colPage = 0;
            int totalColPages = 0;
            string[] rowList = context.Request.Form["rowList"].Split(',');

            if (xmlTable.Attribute("cols-perpage") != null)
            {
                int reservedCols = Convert.ToInt32(xmlTable.Attribute("cols-perpage").Value);
                int colsPerPage = Convert.ToInt32(xmlTable.Attribute("cols-dynamic").Value);
                colPage = Convert.ToInt32(context.Request.Form["colPage"]);
                int x = (dataTable.Columns.Count - reservedCols) % colsPerPage == 0 ? 0 : 1;
                totalColPages = (dataTable.Columns.Count - reservedCols) / colsPerPage + x;

                for (int i = reservedCols, j = reservedCols; j < reservedCols + (colPage - 1) * colsPerPage; j++)
                {
                    dataTable.Columns.RemoveAt(i);
                }

                int colCount = dataTable.Columns.Count;

                for (int i = reservedCols + colsPerPage, j = reservedCols + colsPerPage; j < colCount; j++)
                {
                    dataTable.Columns.RemoveAt(i);
                }
            }

            XElement xButton = xmlTable.Descendants("buttons").FirstOrDefault();

            if (xButton != null)
            {
                appendButtonColumn(xButton, dataTable);
            }

            StringBuilder tableBuilder = new StringBuilder();
            StringBuilder searchBuilder = new StringBuilder();
            
            if (xmlTable.Attribute("cols-perpage") != null)
            {
                appendColPager(tableBuilder, colPage, totalColPages);
            }
            tableBuilder.Append("<table class=\\\"table table-condensed\\\">");
            appendTableHead(xmlTable, dataTable, tableBuilder, searchBuilder);
            appendTableBody(xmlTable, dataTable, tableBuilder);
            tableBuilder.Append("</table>");
            appendPager(xmlTable, tableBuilder, rowList, page, rows, totalRecords, totalPages);

            return "{\"search\":\"" + searchBuilder.ToString() + "\",\"table\":\"" + tableBuilder.ToString() + "\"}";
        }

        static void executeStoredProcedure(HttpRequest request, XElement xmlTable, SqlConnection sqlCon, SqlCommand sqlCmd)
        {
            string[] storedProcedures = xmlTable.Attribute("exec-before").Value.Split(';');

            try
            {
                foreach (var sp in storedProcedures)
                {
                    int index = sp.IndexOf("(");
                    string spStr = "exec " + sp.Substring(0, index) + " ";
                    string[] parameters = sp.Substring(index + 1, sp.Length - index - 2).Split(',');

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        spStr += "'" + request.QueryString[parameters[i]] + "'";

                        if (i < parameters.Length - 1)
                        {
                            spStr += ",";
                        }
                    }

                    sqlCmd.CommandText = spStr;
                    sqlCon.Open();
                    sqlCmd.ExecuteNonQuery();
                }
            }
            catch (Exception exception)
            {
                throw exception;
            }
            finally
            {
                sqlCon.Close();
            }
        }

        static void countRecords(SqlConnection sqlCon, SqlCommand sqlCmd, StringBuilder sqlBuilder, Dictionary<string, string> paramDict, int rows, ref int totalRecords, ref int totalPages)
        {
            sqlCmd.CommandText = "SELECT COUNT(*) " + sqlBuilder.ToString();

            foreach (var pair in paramDict)
            {
                sqlCmd.Parameters.Add(new SqlParameter
                {
                    ParameterName = pair.Key,
                    Value = pair.Value,
                });
            }
            try
            {
                int x = 0;
                sqlCon.Open();
                totalRecords = Convert.ToInt32(sqlCmd.ExecuteScalar());
                x = totalRecords % rows == 0 ? 0 : 1;
                totalPages = totalRecords / rows + x;
            }
            catch (Exception exception)
            {
                throw exception;
            }
            finally
            {
                sqlCon.Close();
            }
        }

        static void appendWhere(HttpContext context, XElement xmlTable, DataTable dataTable, StringBuilder sqlBuilder, Dictionary<string, string> paramDict)
        {
            List<string> queryList = new List<string>();
            bool hasWhere = false;
            bool hasFilter = xmlTable.Attribute("filter") != null;
            bool isAdmin = isAdministrator(context, xmlTable);
            string[] allKeys = context.Request.QueryString.AllKeys;
            string[] filters;
            List<string> ignoredFilters = new List<string>();
            bool containsAllFilter = true;

            if (hasFilter)
            {
                filters = xmlTable.Attribute("filter").Value.Split(',');
                containsAllFilter = allKeys.Intersect(filters).Count() == filters.Count();
            }

            if ((allKeys[0] == null || (hasFilter && !containsAllFilter)) && hasFilter && !isAdmin)
            {
                Exception exception = new Exception("缺少WHERE所需的查询条件");
                throw exception;
            }

            if (xmlTable.Attribute("ignoredfilters") != null)
            {
                ignoredFilters = xmlTable.Attribute("ignoredfilters").Value.Split(',').ToList();
            }

            for (int i = 0; i < allKeys.Length; i++)
            {
                string queryString = allKeys[i];

                if (queryString == null)
                {
                    continue;
                }

                if (isAdmin && hasFilter && ignoredFilters.Contains(queryString))
                {
                    continue;
                }

                string end = queryString.Length > 1 ? queryString.Substring(queryString.Length - 2) : "";
                string sign = string.Empty;
                string paramName = string.Empty;
                string[] paramValueArray = context.Request.QueryString[allKeys[i]].Split(',');
                string paramValue = HttpUtility.HtmlDecode(paramValueArray[paramValueArray.Length - 1]);

                if (end == "==")
                {
                    sign = " LIKE @";
                    queryString = queryString.Remove(queryString.Length - 2);
                    paramName = queryString;
                    paramValue = "%" + paramValue + "%";
                }
                else if (end == ">=")
                {
                    sign = end + "@";
                    queryString = queryString.Remove(queryString.Length - 2);
                    paramName = queryString + "Min";
                }
                else if (end == "<=")
                {
                    sign = end + "@";
                    queryString = queryString.Remove(queryString.Length - 2);
                    paramName = queryString + "Max";
                }
                else
                {
                    sign = "=@";
                    paramName = queryString;
                }

                if (!dataTable.Columns.Contains(queryString))
                {
                    continue;
                }

                if (paramDict.ContainsKey("@" + queryString))
                {
                    continue;
                }

                if (!hasWhere)
                {
                    sqlBuilder.Append(" WHERE ");
                    hasWhere = true;
                }

                XElement xmlCol = xmlTable.Descendants(paramName).FirstOrDefault();

                paramName = Regex.Replace(paramName, @"[^\w]", "");

                if (!queryList.Contains(queryString) || (queryList.Contains(queryString) && !paramDict.ContainsKey("@" + paramName)))
                {
                    queryList.Add(queryString);
                    sqlBuilder.Append("[" + queryString + "]" + sign + paramName);
                    sqlBuilder.Append(" AND ");
                }

                if (!paramDict.ContainsKey("@" + paramName))
                {
                    if (xmlCol != null && xmlCol.Attribute("encrypted") != null)
                    {
                        paramValue = decrypt(paramValue);
                    }

                    paramDict.Add("@" + paramName, paramValue);
                }
                else
                {
                    paramDict["@" + paramName] = paramValue;
                }
            }

            if (hasWhere && allKeys.Length > 0)
            {
                sqlBuilder.Remove(sqlBuilder.Length - 5, 5);
            }
        }

        static string appendOrderBy(XElement xmlTable, DataTable dataTable, string orderBy, string xsc)
        {
            if (!String.IsNullOrEmpty(orderBy))
            {
                if (!dataTable.Columns.Contains(orderBy))
                {
                    NullReferenceException exception = new NullReferenceException("数据表不包含欲排序列 [" + orderBy + "]");
                    throw exception;
                }
                else if (xsc != "ASC" && xsc != "DESC")
                {
                    NullReferenceException exception = new NullReferenceException("order 必须为 \"ASC\" 或 \"DESC\"");
                    throw exception;
                }

                return "ORDER BY [" + orderBy + "] " + xsc;
            }
            else if (xmlTable.Attribute("defaultorder") != null)
            {
                return "ORDER BY " + xmlTable.Attribute("defaultorder").Value;
            }

            return string.Empty;
        }

        static void appendTableHead(XElement xmlTable, DataTable dataTable, StringBuilder tableBuilder, StringBuilder searchBuilder)
        {
            tableBuilder.Append("<thead>");
            tableBuilder.Append("<tr>");

            if (xmlTable.Attribute("checkbox") != null)
            {
                tableBuilder.Append("<th class=\\\"rt-th-checkbox\\\" name=\\\"rt-th-checkbox\\\">");
                tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");
                tableBuilder.Append("<input type=\\\"checkbox\\\" class=\\\"rt-checkbox\\\"/>");
                tableBuilder.Append("</div>");
                tableBuilder.Append("</th>");
            }

            foreach (DataColumn col in dataTable.Columns)
            {
                string colName = col.ToString();
                XElement xmlCol;

                if (!isBeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
                {
                    xmlCol = xmlTable.Descendants(colName).FirstOrDefault();
                }
                else
                {
                    xmlCol = xmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
                }

                tableBuilder.Append("<th");

                if (xmlCol != null && xmlCol.Attribute("visibility") != null && xmlCol.Attribute("visibility").Value == "hidden")
                {
                    tableBuilder.Append(" class=\\\"hiddenCol\\\"");
                }

                tableBuilder.Append(" name=\\\"" + colName + "\\\">");

                if (xmlCol != null && xmlCol.Attribute("text") != null)
                {
                    tableBuilder.Append(xmlCol.Attribute("text").Value);
                }
                else
                {
                    tableBuilder.Append(colName);
                }

                tableBuilder.Append("</th>");

                if (colName == "操作")
                {
                    continue;
                }

                if (xmlCol != null && xmlCol.Attribute("search-type") != null)
                {
                    if (xmlCol.Attribute("search-type").Value == "range" || xmlCol.Attribute("search-type").Value == "date")
                    {
                        searchBuilder.Append("<div>");

                        if (xmlCol != null && xmlCol.Attribute("text") != null)
                        {
                            searchBuilder.Append("<div class=\\\"rt-search-heading\\\">" + xmlCol.Attribute("text").Value + "：" + "</div>");
                        }
                        else
                        {
                            searchBuilder.Append("<div class=\\\"rt-search-heading\\\">" + colName + "：" + "</div>");
                        }

                        searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt " + xmlCol.Attribute("search-type").Value + "\\\" name=\\\"" + colName + ">%3d\\\"/>");
                        searchBuilder.Append("<span class=\\\"search-span-minus\\\"> - </span>");
                        searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt " + xmlCol.Attribute("search-type").Value + "\\\" name=\\\"" + colName + "<%3d\\\"/>");
                        searchBuilder.Append("</div>");
                    }
                    else if (xmlCol.Attribute("search-type").Value == "none")
                    {
                        //search-type为none时，不生成查询框
                    }
                }
                else if (xmlCol == null || xmlCol.Attribute("visibility") == null || xmlCol.Attribute("visibility").Value != "hidden") //没有配置的列，或者有配置但没隐藏的列，生成默认查询框（隐藏列不生成查询框）
                {
                    searchBuilder.Append("<div>");

                    if (xmlCol != null && xmlCol.Attribute("text") != null)
                    {
                        searchBuilder.Append("<div class=\\\"rt-search-heading\\\">" + xmlCol.Attribute("text").Value + "：" + "</div>");
                    }
                    else
                    {
                        searchBuilder.Append("<div class=\\\"rt-search-heading\\\">" + colName + "：" + "</div>");
                    }

                    searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt");

                    if (xmlCol != null && xmlCol.Attribute("search-btn") != null && xmlCol.Attribute("search-btn-func") != null)
                    {
                        searchBuilder.Append(" sbtn\\\" name=\\\"" + colName + "%3d%3d\\\"/>");
                        searchBuilder.Append("<span class=\\\"glyphicon glyphicon-" + xmlCol.Attribute("search-btn").Value + "\\\" onclick=\\\"" + xmlCol.Attribute("search-btn-func").Value + "\\\"></span>");
                    }
                    else
                    {
                        searchBuilder.Append("\\\" name=\\\"" + colName + "%3d%3d\\\"/>");
                    }

                    searchBuilder.Append("</div>");
                }
            }

            tableBuilder.Append("</tr>");
            tableBuilder.Append("</thead>");
        }
        static void appendTab(XElement xmlTable, DataTable dataTable, StringBuilder tabBuilder)
        {
            tabBuilder.Append("<ul class=\\\"rt-tab\\\">");
            string[] fieldName = xmlTable.Attribute("tabby").Value.Split(',');
            DataTable dataTableDistinct = SelectDistinct(dataTable, fieldName);
            foreach (DataRow row in dataTableDistinct.Rows)
            {
                tabBuilder.Append("<li calss=" + row[fieldName[0]] + ">");
                tabBuilder.Append("<span class=\\\"rt-tab-span\\\" data-name=\\\""+fieldName[0]+"\\\" data-value=\\\""+row[fieldName[0]]+"\\\">" + row[fieldName[0]] + "</span>");
                tabBuilder.Append("</li>");
            };
            tabBuilder.Append("</ul>");
        }
        static void appendTableBody(XElement xmlTable, DataTable dataTable, StringBuilder tableBuilder)
        {
            tableBuilder.Append("<tbody>");

            foreach (DataRow row in dataTable.Rows)
            {
                tableBuilder.Append("<tr>");

                if (xmlTable.Attribute("checkbox") != null)
                {
                    tableBuilder.Append("<td class=\\\"rt-td-checkbox\\\" name=\\\"rt-td-checkbox\\\" data-value=\\\"" + row[dataTable.Columns[0]] + "\\\">");
                    tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");

                    if (!dataTable.Columns.Contains(xmlTable.Attribute("checkbox").Value))
                    {
                        Exception excepion = new Exception("checkbox所指列不存在");
                        throw excepion;
                    }

                    tableBuilder.Append("<input type=\\\"checkbox\\\"  class=\\\"rt-checkbox\\\" value=\\\"" + row[xmlTable.Attribute("checkbox").Value] + "\\\" />");
                    tableBuilder.Append("</div>");
                    tableBuilder.Append("</td>");
                }

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ToString();
                    XElement xmlCol;

                    if (!isBeginWithNumber(colName))
                    {
                        xmlCol = xmlTable.Descendants(colName).FirstOrDefault();
                    }
                    else
                    {
                        xmlCol = xmlTable.Descendants("bwn" + colName).FirstOrDefault();
                    }
                    if (xmlCol != null && xmlCol.Attribute("filter") != null && !xmlCol.Attribute("filter").Value.Contains(HttpContext.Current.Session["identitystate"].ToString()))
                    {
                        continue;
                    }
                    tableBuilder.Append("<td name=\\\"" + colName + "\\\"");

                    if (xmlCol != null && xmlCol.Attribute("visibility") != null && xmlCol.Attribute("visibility").Value == "hidden")
                    {
                        tableBuilder.Append(" class=\\\"hiddenCol\\\"");
                    }

                    if (xmlCol != null && xmlCol.Attribute("linkto") != null && xmlCol.Attribute("passedcol") != null)
                    {
                        tableBuilder.Append(" data-table=\\\"" + xmlCol.Attribute("linkto").Value + "\\\"");
                        tableBuilder.Append(" data-passedcol=\\\"");

                        string[] passedCol = xmlCol.Attribute("passedcol").Value.Split(',');

                        for (int i = 0; i < passedCol.Length; i++)
                        {
                            tableBuilder.Append(passedCol[i] + "=" + row[passedCol[i]]);

                            if (i < passedCol.Length - 1)
                            {
                                tableBuilder.Append("&");
                            }
                        }

                        tableBuilder.Append("\\\"");

                        if (xmlCol.Attribute("navname") != null)
                        {
                            tableBuilder.Append(" data-navname=\\\"" + row[xmlCol.Attribute("navname").Value] + "\\\"");
                        }
                    }

                    string cellValue = row[col].ToString();
                    cellValue = transformFormat(row, xmlCol, cellValue);

                    if (colName != "操作")
                    {
                        tableBuilder.Append(" data-value=\\\"" + cellValue + "\\\">" + FormatCell(dataTable.Columns, row, xmlCol, colName, cellValue) + "</td>");
                    }
                    else
                    {
                        tableBuilder.Append(">" + cellValue + "</td>");
                    }
                }

                tableBuilder.Append("</tr>");
            }

            tableBuilder.Append("</tbody>");
        }

        static void appendPager(XElement xmlTable, StringBuilder tableBuilder, string[] rowList, int page, int rows, int totalRecords, int totalPages)
        {
            tableBuilder.Append("<div class=\\\"rt-pager-container\\\">");

            tableBuilder.Append("<div class=\\\"rt-pager-buttons\\\">");

            if (xmlTable.Attribute("search") == null || xmlTable.Attribute("search").Value != "false")
            {
                tableBuilder.Append("<span class=\\\"rt-pager-search rt-pager-btn\\\"><span class=\\\"glyphicon glyphicon-search\\\" title=\\\"查询\\\"></span>查询</span>");
            }
            if (xmlTable.Attribute("excel") != null && xmlTable.Attribute("excel").Value == "true")
            {
                tableBuilder.Append("<span class=\\\" rt-pager-export rt-pager-btn\\\"><span class=\\\"glyphicon glyphicon-export\\\" title=\\\"导出Excel\\\"></span>导出</span>");
            }

            XElement pagerButtons = xmlTable.Descendants("pagerbuttons").FirstOrDefault();
            if (pagerButtons != null)
            {
                var btns = pagerButtons.Nodes();
                foreach (XElement btn in btns)
                {
                    var btnStr = btn.ToString().Replace("\"", "\\\"");
                    var elemStr = Regex.Replace(Regex.Replace(btnStr, @"\s+<", @"<"), @">\s+", @">");
                    tableBuilder.Append(elemStr);
                }
            }

            tableBuilder.Append("</div>");

            tableBuilder.Append("<div class=\\\"rt-pager-controls\\\">");
            tableBuilder.Append("&nbsp;<span class=\\\"glyphicon glyphicon-step-backward rt-pager-firstPage\\\"></span>&nbsp;<span class=\\\"glyphicon glyphicon-backward rt-pager-prevPage\\\"></span>");
            tableBuilder.Append("&nbsp;<span class=\\\"pager-separator\\\"></span>&nbsp;");
            tableBuilder.Append("第&nbsp;<input type=\\\"text\\\" class=\\\"rt-pager-page\\\" value=\\\"" + page + "\\\"/>&nbsp;页，共&nbsp;<span class=\\\"rt-pager-totalPages\\\">" + totalPages + "</span>&nbsp;页");
            tableBuilder.Append("&nbsp;<span class=\\\"pager-separator\\\"></span>&nbsp;");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-forward rt-pager-nextPage\\\"></span>&nbsp;<span class=\\\"glyphicon glyphicon-step-forward rt-pager-lastPage\\\"></span>&nbsp;&nbsp;");
            tableBuilder.Append("<select class=\\\"rt-pager-rowList\\\">");
            tableBuilder.Append("</div>");

            foreach (string i in rowList)
            {
                tableBuilder.Append("<option value=\\\"" + i + "\\\"");

                if (Convert.ToInt32(i) == rows)
                {
                    tableBuilder.Append(" selected");
                }

                tableBuilder.Append(">" + i + "</option>");
            }

            tableBuilder.Append("</select>");
            tableBuilder.Append("<span class=\\\"rt-pager-records\\\">第&nbsp;" + ((page - 1) * rows + 1) + " - " + ((page * rows) <= totalRecords ? (page * rows) : totalRecords) + "&nbsp;条，");
            tableBuilder.Append("共&nbsp;<span class=\\\"rt-pager-totalRecords\\\">" + totalRecords + "</span>&nbsp;条</span>");

            tableBuilder.Append("</div>");
        }

        static void appendColPager(StringBuilder tableBuilder, int colPage, int totalColPages)
        {
            tableBuilder.Append("<div class=\\\"rt-colPager-container\\\">");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-chevron-left rt-colPager-prev\\\"></span>");
            tableBuilder.Append("<input type=\\\"hidden\\\" class=\\\"rt-colPager-page\\\" value=\\\"" + colPage + "\\\"/>");
            tableBuilder.Append("<input type=\\\"hidden\\\" class=\\\"rt-colPager-totalColPages\\\" value=\\\"" + totalColPages + "\\\"/>");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-chevron-right rt-colPager-next\\\"></span>");
            tableBuilder.Append("</div>");
        }

        static void appendButtonColumn(XElement xButton, DataTable dataTable)
        {
            dataTable.Columns.Add("操作");
            var btns = xButton.Descendants();

            foreach (DataRow row in dataTable.Rows)
            {
                foreach (XElement btn in btns)
                {
                    if (btn.Attribute("linkto") != null && btn.Attribute("passedcol") != null)
                    {
                        btn.SetAttributeValue("data-table", btn.Attribute("linkto").Value);

                        string[] passedCol = btn.Attribute("passedcol").Value.Split(',');
                        string qryStr = string.Empty;

                        for (int i = 0; i < passedCol.Length; i++)
                        {
                            qryStr += passedCol[i] + "=" + row[passedCol[i]];

                            if (i < passedCol.Length - 1)
                            {
                                qryStr += "&";
                            }
                        }

                        btn.SetAttributeValue("data-passedCol", qryStr);
                    }

                    if (btn.Attribute("navname") != null)
                    {
                        btn.SetAttributeValue("data-navname", row[btn.Attribute("navname").Value]);
                    }
                }

                row["操作"] = String.Join("", xButton.Elements()).Replace("\"", "\\\"");
            }
        }

        static void exportExcel(XElement xmlTable, DataTable dataTable, SqlDataAdapter dataAdapter, StringBuilder sqlBuilder, Dictionary<string, string> paramDict, MemoryStream excelStream)
        {
            dataAdapter.SelectCommand.CommandText = "SELECT * " + sqlBuilder.ToString();

            foreach (var pair in paramDict)
            {
                dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                {
                    ParameterName = pair.Key,
                    Value = pair.Value,
                });
            }

            dataTable.PrimaryKey = null;
            dataAdapter.Fill(dataTable);
            dataTable.Columns.Remove("RowNumber");

            string sheetName = "sheet1";
            DataClassesDataContext db = new DataClassesDataContext();
            IQueryable<t_imsExcelField> excelField;
            var excelTable = db.t_imsExcelTables.FirstOrDefault(n => n.tablename == dataTable.TableName);

            if (excelTable != null)
            {
                sheetName = excelTable.cname;
                excelField = from n in db.t_imsExcelFields where n.tableid == excelTable.OBJ_ID select n;
            }
            else
            {
                excelField = from n in db.t_imsExcelFields where n.tableid == -1 select n;
            }


            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet(sheetName);
            IRow th = sheet.CreateRow(0);

            for (int i = 0, j = 0; i < dataTable.Columns.Count; i++)
            {
                XElement xmlCol;
                string colName = dataTable.Columns[i].ToString();

                if (!isBeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
                {
                    xmlCol = xmlTable.Descendants(colName).FirstOrDefault();
                }
                else
                {
                    xmlCol = xmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
                }

                if (xmlCol != null && xmlCol.Attribute("export2excel") != null && xmlCol.Attribute("export2excel").Value == "false")
                {
                    dataTable.Columns.Remove(colName);
                    i--;
                }
                else
                {
                    var ef = excelField.FirstOrDefault(n => n.fieldname == colName);

                    if (ef != null)
                    {
                        th.CreateCell(j).SetCellValue(ef.cname);
                    }
                    else if (xmlCol != null && xmlCol.Attribute("text") != null)
                    {
                        th.CreateCell(j).SetCellValue(xmlCol.Attribute("text").Value);
                    }
                    else
                    {
                        th.CreateCell(j).SetCellValue(colName);
                    }

                    j++;
                }
            }

            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                IRow row = sheet.CreateRow(i + 1);

                for (int j = 0; j < dataTable.Columns.Count; j++)
                {
                    XElement xmlCol;
                    string colName = dataTable.Columns[j].ToString();

                    if (!isBeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
                    {
                        xmlCol = xmlTable.Descendants(colName).FirstOrDefault();
                    }
                    else
                    {
                        xmlCol = xmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
                    }

                    string cellValue = dataTable.Rows[i][j].ToString();
                    cellValue = transformFormat(dataTable.Rows[i], xmlCol, cellValue);

                    row.CreateCell(j).SetCellValue(cellValue);
                }
            }

            workbook.Write(excelStream);
        }

        static bool isAdministrator(HttpContext context, XElement xmlTable)
        {
            string identity = context.Session["identitystate"].ToString();
            string staffNo = context.Session["Uname"].ToString();

            XAttribute admin = xmlTable.Attribute("admin");

            if (admin == null)
            {
                return false;
            }

            string[] adminArray = admin.Value.Split(',');

            if (adminArray.Contains(identity) || adminArray.Contains(staffNo))
            {
                return true;
            }

            return false;
        }

        static bool isBeginWithNumber(string colName)
        {
            int k = 0;
            return Int32.TryParse(colName[0].ToString(), out k);
        }

        static string transformFormat(DataRow row, XElement xmlCol, string cellValue)
        {
            string outPutValue = string.Empty;
            bool isEmpty = String.IsNullOrWhiteSpace(cellValue);
            bool isInXml = xmlCol != null;

            if (!isEmpty && isInXml && xmlCol.Attribute("dateformat") != null)
            {
                outPutValue = Convert.ToDateTime(cellValue).ToString(xmlCol.Attribute("dateformat").Value);
            }
            else if (!isEmpty && isInXml && xmlCol.Attribute("precision") != null)
            {
                double val;

                if (Double.TryParse(cellValue, out val))
                {
                    bool percentageform = xmlCol.Attribute("percentageform") != null && xmlCol.Attribute("percentageform").Value == "true";

                    if (percentageform)
                    {
                        val = val * 100;
                    }

                    outPutValue = val.ToString(xmlCol.Attribute("precision").Value);

                    if (percentageform)
                    {
                        outPutValue += "%";
                    }
                }
                else
                {
                    outPutValue = cellValue;
                }
            }
            else if (!isEmpty && isInXml && xmlCol.Attribute("timetransfer") != null)
            {
                switch (xmlCol.Attribute("timetransfer").Value)
                {
                    case "second":
                        int val;
                        if (int.TryParse(cellValue, out val))
                        {
                            outPutValue = (val / (24 * 60 * 60)).ToString() + "日"
                                             + ((val - (val / (24 * 60 * 60)) * 60 * 60 * 24) / 3600).ToString() + "时"
                                             + ((val - (val / (60 * 60)) * 60 * 60) / 60).ToString() + "分"
                                             + (val - (val / 60) * 60).ToString() + "秒";
                        }
                        else
                        {
                            outPutValue = cellValue;
                        }
                        break;
                    default: outPutValue = cellValue;
                        break;
                }
            }
            else
            {
                outPutValue = cellValue;
            }

            if (isInXml && xmlCol.Attribute("regex-pattern") != null && xmlCol.Attribute("regex-replacement") != null)
            {
                outPutValue = Regex.Replace(outPutValue, xmlCol.Attribute("regex-pattern").Value, xmlCol.Attribute("regex-replacement").Value);
            }

            return outPutValue;
        }

        static string FormatCell(DataColumnCollection columns, DataRow row, XElement xmlCol, string colName, string cellValue)
        {
            string formattedCell = cellValue;
            bool isInXml = xmlCol != null;

            if (isInXml && xmlCol.Attribute("formatter") != null)
            {
                string formatterName = xmlCol.Attribute("formatter").Value;
                Dictionary<string, string> colsDict = new Dictionary<string, string>();
                KeyValuePair<string, string> currentCell = new KeyValuePair<string, string>(colName, cellValue);

                foreach (var col in columns)
                {
                    string colStr = col.ToString();
                    colsDict.Add(colStr, row[colStr].ToString());
                }

                Type cellFormatter = typeof(CellFormatter);
                MethodInfo formatter = cellFormatter.GetMethod(formatterName);
                formattedCell = formatter.Invoke(null, new object[] { currentCell, colsDict }).ToString();
            }

            return formattedCell;
        }

        public static string encrypt(string plainText, string pwh = "default", string sk = "default", string vk = "default")
        {
            string passwordHash = string.Empty;
            string saltKey = string.Empty;
            string viKey = string.Empty;

            if (pwh == "default")
            {
                passwordHash = (HttpContext.Current.Session["PasswordHash"] ?? defaultPasswordHash).ToString();
            }
            else
            {
                passwordHash = pwh;
            }
            if (sk == "default")
            {
                saltKey = (HttpContext.Current.Session["SaltKey"] ?? defaultSaltKey).ToString();
            }
            else
            {
                saltKey = sk;
            }
            if (vk == "default")
            {
                viKey = (HttpContext.Current.Session["VIKey"] ?? defaultVIKey).ToString();
            }
            else
            {
                viKey = vk;
            }

            byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherTextBytes;
            byte[] keyBytes = new Rfc2898DeriveBytes(passwordHash, Encoding.ASCII.GetBytes(saltKey)).GetBytes(256 / 8);

            RijndaelManaged symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.Zeros };
            ICryptoTransform encryptor = symmetricKey.CreateEncryptor(keyBytes, Encoding.ASCII.GetBytes(viKey));

            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                    cryptoStream.FlushFinalBlock();
                    cipherTextBytes = memoryStream.ToArray();
                }
            }

            return Convert.ToBase64String(cipherTextBytes);
        }

        public static string decrypt(string encryptedText, string pwh = "default", string sk = "default", string vk = "default")
        {
            string passwordHash = string.Empty;
            string saltKey = string.Empty;
            string viKey = string.Empty;

            if (pwh == "default")
            {
                passwordHash = (HttpContext.Current.Session["PasswordHash"] ?? defaultPasswordHash).ToString();
            }
            else
            {
                passwordHash = pwh;
            }
            if (sk == "default")
            {
                saltKey = (HttpContext.Current.Session["SaltKey"] ?? defaultSaltKey).ToString();
            }
            else
            {
                saltKey = sk;
            }
            if (vk == "default")
            {
                viKey = (HttpContext.Current.Session["VIKey"] ?? defaultVIKey).ToString();
            }
            else
            {
                viKey = vk;
            }

            byte[] cipherTextBytes = Convert.FromBase64String(encryptedText);
            byte[] plainTextBytes = new byte[cipherTextBytes.Length];
            byte[] keyBytes = new Rfc2898DeriveBytes(passwordHash, Encoding.ASCII.GetBytes(saltKey)).GetBytes(256 / 8);
            int decryptedByteCount = 0;

            RijndaelManaged symmetricKey = new RijndaelManaged() { Mode = CipherMode.CBC, Padding = PaddingMode.None };
            ICryptoTransform decryptor = symmetricKey.CreateDecryptor(keyBytes, Encoding.ASCII.GetBytes(viKey));

            using (MemoryStream memoryStream = new MemoryStream(cipherTextBytes))
            {
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                {
                    decryptedByteCount = cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
                }
            }

            return Encoding.UTF8.GetString(plainTextBytes, 0, decryptedByteCount).TrimEnd("\0".ToCharArray());
        }
        public static DataTable SelectDistinct(DataTable sourceTable, params string[] fieldName)
        {
            DataView dataView = sourceTable.DefaultView;
            return dataView.ToTable(true, fieldName);//注：其中ToTable（）的第一个参数为是否DISTINCT
        }
    }
}