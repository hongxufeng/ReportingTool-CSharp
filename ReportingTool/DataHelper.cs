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
using System.Web.Script.Serialization;

namespace IMSROOT.ReportingTool
{
    public class DataHelper : IRequiresSessionState
    {
        static readonly string defaultPasswordHash = "CAXACAXA";
        static readonly string defaultSaltKey = "CAXACAXA";
        static readonly string defaultVIKey = "CAXACAXACAXACAXA";

        public static string GetTable(HttpContext context, MemoryStream excelStream, string cmd = "GetTable")
        {
            string tableID = HttpUtility.UrlDecode(context.Request["table"]);
            string style = context.Request["style"];

            XDocument xml = XDocument.Load(context.Request.PhysicalApplicationPath + "ReportingTool\\xml\\" + context.Request["ConfigFile"] + ".xml");
            XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == tableID select n).FirstOrDefault();
            Params prms = new Params(context, xmlTable, IsAdministrator(context, xmlTable));

            if (prms.XmlTable == null)
            {
                Exception exception = new Exception("数据表配置\"" + tableID + "\"不存在");
                throw exception;
            }

            string table = GetTableName(prms);
            string connectionConfig = "PLMConnectionString";

            if (prms.XmlTable.Attribute("connectionstring") != null)
            {
                connectionConfig = prms.XmlTable.Attribute("connectionstring").Value;
            }

            string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[connectionConfig].ConnectionString;
            SqlConnection sqlCon = new SqlConnection(connectionString);
            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.Connection = sqlCon;

            if (prms.XmlTable.Attribute("exec-before") != null)
            {
                ExecuteStoredProcedure(prms, sqlCon, sqlCmd);
            }

            SqlDataAdapter dataAdapter = new SqlDataAdapter("SELECT * FROM " + table, connectionString);
            Dictionary<string, string> paramDict = new Dictionary<string, string>();

            dataAdapter.SelectCommand.CommandText = "SELECT * FROM " + table;
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

            XElement firstColumn = prms.XmlTable.Descendants().FirstOrDefault();
            if (firstColumn == null)
            {
                Exception exception = new Exception("数据表配置中至少要包含一列");
                throw exception;
            }
            else if (prms.XmlTable.Nodes().Count() == 1 && (firstColumn.Name == "pagerbuttons" || firstColumn.Name == "buttons"))
            {
                Exception exception = new Exception("数据表配置中至少要包含一列");
                throw exception;
            }

            int page = Convert.ToInt32(context.Request.QueryString["page"]);
            int rows = Convert.ToInt32(context.Request.QueryString["rows"]);
            string sort = context.Request.QueryString["sort"];

            string order = string.Empty;
            List<string> sortList = new List<string>();

            if (!String.IsNullOrEmpty(sort))
            {
                sortList = sort.Split(' ').ToList();
                order = "ORDER BY [" + sortList[0] + "] " + sortList[1];
            }
            else if (prms.XmlTable.Attribute("defaultorder") != null)
            {
                order = "ORDER BY " + prms.XmlTable.Attribute("defaultorder").Value;
            }
            else
            {
                order = "ORDER BY [" + firstColumn.Name + "] ASC";
            }

            StringBuilder sqlBuilder = new StringBuilder();
            List<string> queryList = new List<string>();

            sqlBuilder.Append("FROM (");
            sqlBuilder.Append("SELECT ROW_NUMBER() OVER(" + order + ") AS RowNumber, * FROM " + table);
            AppendWhere(prms, dataTable, sqlBuilder, queryList, paramDict);
            sqlBuilder.Append(") AS gridTable");

            int totalRecords = 0;
            int totalPages = 0;

            if (style != Style.Tree && cmd != "exportExcel")
            {
                CountRecords(sqlCon, sqlCmd, sqlBuilder, paramDict, rows, ref totalRecords, ref totalPages);
            }

            if (cmd == "ExportExcel")
            {
                ExportExcel(prms, dataTable, dataAdapter, sqlBuilder, paramDict, excelStream);
                return null;
            }

            string sql = sqlBuilder.ToString();
            dataAdapter.SelectCommand.CommandText = "SELECT * " + sql;

            dataAdapter.SelectCommand.Parameters.Clear();

            foreach (var pair in paramDict)
            {
                dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                {
                    ParameterName = pair.Key,
                    Value = pair.Value,
                });
            }

            if (style != Style.Tree)
            {
                dataAdapter.SelectCommand.CommandText += " WHERE RowNumber BETWEEN " + ((page - 1) * rows + 1) + " AND " + page * rows;
            }

            dataTable.PrimaryKey = null;
            dataAdapter.Fill(dataTable);
            dataTable.Columns.Remove("RowNumber");

            int colPage = 0;
            int totalColPages = 0;
            string[] rowList = null;
            if (style != Style.Tree)
            {
                rowList = context.Request["rowList"].Split(',');
            }

            if (prms.XmlTable.Attribute("cols-perpage") != null)
            {
                int reservedCols = Convert.ToInt32(prms.XmlTable.Attribute("cols-perpage").Value);
                int colsPerPage = Convert.ToInt32(prms.XmlTable.Attribute("cols-dynamic").Value);
                colPage = Convert.ToInt32(context.Request["colpage"]);
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

            XElement xButton = prms.XmlTable.Descendants("buttons").FirstOrDefault();

            if (xButton != null)
            {
                dataTable.Columns.Add("操作");
            }

            StringBuilder bodyBuilder = new StringBuilder();
            StringBuilder searchBuilder = new StringBuilder();
            StringBuilder rowBuilder = new StringBuilder();

            if (prms.XmlTable.Attribute("cols-perpage") != null)
            {
                BuildColPager(bodyBuilder, colPage, totalColPages);
            }

            if (style == Style.Rect)
            {
                BuildRectHead(prms, dataTable, bodyBuilder, searchBuilder, sortList, page, totalPages);
                BuildRectBody(prms, dataTable, bodyBuilder);
                BuildTablePager(prms, bodyBuilder, rowList, page, rows, totalRecords, totalPages, Style.Rect);
                BuildNullRow(prms, dataTable, rowBuilder);
            }
            else if (style == Style.Tree)
            {
                foreach (DataColumn col in dataTable.Columns)
                {
                    XElement xmlCol = null;
                    string colName = col.ToString();
                    SetColConfig(colName, ref xmlCol, prms, dataTable);
                }
                bodyBuilder = new StringBuilder();
                BuildTablePager(prms, bodyBuilder, rowList, page, rows, totalRecords, totalPages, Style.Tree);
                BuildTree(prms, dataTable, bodyBuilder);
            }
            else
            {
                bodyBuilder.Append("<table class=\\\"table table-condensed\\\">");
                BuildTableHead(prms, dataTable, bodyBuilder, searchBuilder, sortList);
                BuildTableBody(prms, dataTable, bodyBuilder);
                bodyBuilder.Append("</table>");
                BuildTablePager(prms, bodyBuilder, rowList, page, rows, totalRecords, totalPages, Style.Table);
                BuildNullRow(prms, dataTable, rowBuilder);
            }

            StringBuilder selectorBuilder = new StringBuilder();
            StringBuilder conditionBuilder = new StringBuilder();

            if (style == Style.Table)
            {
                BuildSelectorBar(prms, dataAdapter, sql, queryList, selectorBuilder, conditionBuilder);
            }

            return "{\"search\":\"" + searchBuilder.ToString() + "\",\"body\":\"" + bodyBuilder.ToString() + "\",\"selector\":\"" + selectorBuilder.ToString() + "\",\"condition\":\"" + conditionBuilder.ToString() + "\",\"row\":\"" + rowBuilder.ToString() + "\"}";
        }

        public static string SearchTree(HttpContext context)
        {
            string tableID = HttpUtility.UrlDecode(context.Request["table"]);
            string condition = context.Request["condition"];
            XDocument xml = XDocument.Load(context.Request.PhysicalApplicationPath + "ReportingTool\\xml\\" + context.Request["ConfigFile"] + ".xml");
            Dictionary<string, string> sqlDict = new Dictionary<string, string>();

            BuildSearchingSql(context, xml, tableID, sqlDict);

            List<string> finalHtml = new List<string>();

            foreach (var sql in sqlDict)
            {
                XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == sql.Key select n).First();
                Params prms = new Params(context, xmlTable, IsAdministrator(context, xmlTable));

                string connectionConfig = "PLMConnectionString";

                if (prms.XmlTable.Attribute("connectionstring") != null)
                {
                    connectionConfig = prms.XmlTable.Attribute("connectionstring").Value;
                }

                string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[connectionConfig].ConnectionString;
                SqlConnection sqlCon = new SqlConnection(connectionString);
                SqlCommand sqlCmd = new SqlCommand();
                sqlCmd.Connection = sqlCon;

                if (prms.XmlTable.Attribute("exec-before") != null)
                {
                    ExecuteStoredProcedure(prms, sqlCon, sqlCmd);
                }

                SqlDataAdapter dataAdapter = new SqlDataAdapter(sql.Value, connectionString);

                var visibleCols = from n in prms.XmlTable.Elements() where n.Attribute("visibility") != null && n.Attribute("visibility").Value == "visible" select n;

                foreach (var col in visibleCols)
                {
                    dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                    {
                        ParameterName = "@" + col.Name,
                        Value = "%" + condition + "%"
                    });
                }

                DataTable dataTable = new DataTable();
                dataTable.PrimaryKey = null;
                dataAdapter.Fill(dataTable);

                if (dataTable.Rows.Count == 0)
                {
                    continue;
                }

                StringBuilder bodyBuilder = new StringBuilder();
                StringBuilder searchBuilder = new StringBuilder();
                List<string> sortList = new List<string>();

                foreach (DataColumn col in dataTable.Columns)
                {
                    XElement xmlCol = null;
                    string colName = col.ToString();
                    SetColConfig(colName, ref xmlCol, prms, dataTable);
                }

                bodyBuilder = new StringBuilder();
                bodyBuilder.Append("<div class=\\\"rt-search-result\\\">" + prms.XmlTable.Attribute("id").Value);
                BuildTree(prms, dataTable, bodyBuilder);
                bodyBuilder.Append("</div>");
                finalHtml.Add(bodyBuilder.ToString());
            }

            return "{\"body\":\"" + String.Join("", finalHtml) + "\"}";
        }

        public static string LocateNode(HttpContext context)
        {
            XDocument xml = XDocument.Load(context.Request.PhysicalApplicationPath + "ReportingTool\\xml\\" + context.Request["ConfigFile"] + ".xml");
            List<string> result = new List<string>();
            string tableID = HttpUtility.UrlDecode(context.Request["table"]);
            string condition = context.Request["condition"];
            BuildLocationsResult(context, xml, tableID, condition, result);
            result.Reverse();

            return "[" + String.Join(",", result) + "]";
        }

        static void AppendWhere(Params prms, DataTable dataTable, StringBuilder sqlBuilder, List<string> queryList, Dictionary<string, string> paramDict)
        {
            bool hasWhere = false;
            bool hasFilter = prms.XmlTable.Attribute("filter") != null;
            string[] allKeys = prms.Context.Request.QueryString.AllKeys;
            string[] filters;
            List<string> ignoredFilters = new List<string>();
            bool containsAllFilter = true;
            Dictionary<string, string> signDict = new Dictionary<string, string>()
            {
                {" LIKE @","LK"},{">=@","GT"},{"<=@","LT"},{"=@","EQ"}
            };

            if (hasFilter)
            {
                filters = prms.XmlTable.Attribute("filter").Value.Split(',');
                containsAllFilter = allKeys.Intersect(filters).Count() == filters.Count();
            }

            if ((allKeys[0] == null || (hasFilter && !containsAllFilter)) && hasFilter && !prms.IsAdmin)
            {
                Exception exception = new Exception("缺少WHERE所需的查询条件");
                throw exception;
            }

            if (prms.XmlTable.Attribute("ignoredfilters") != null)
            {
                ignoredFilters = prms.XmlTable.Attribute("ignoredfilters").Value.Split(',').ToList();
            }

            for (int i = 0; i < allKeys.Length; i++)
            {
                string queryString = allKeys[i];

                if (queryString == null)
                {
                    continue;
                }

                string[] paramValueArray = HttpUtility.UrlDecode(prms.Context.Request.QueryString[allKeys[i]]).Split('|');
                int pos = queryString.Length > 1 ? 2 : 0;
                string sign = queryString.Substring(queryString.Length - pos);
                string paramName = string.Empty;

                if (prms.IsAdmin && ignoredFilters.Contains(queryString) && String.IsNullOrWhiteSpace(paramValueArray[0]))
                {
                    continue;
                }

                if (sign == "~~")
                {
                    sign = " LIKE @";
                    queryString = queryString.Substring(0, queryString.Length - 2);
                    paramName = queryString;
                    for (int p = 0; p < paramValueArray.Length; p++)
                    {
                        paramValueArray[p] = "%" + paramValueArray[p] + "%";
                    }
                }
                else if (sign == ">=")
                {
                    sign = sign + "@";
                    queryString = queryString.Substring(0, queryString.Length - 2);
                    paramName = queryString + "Min";
                }
                else if (sign == "<=")
                {
                    sign = sign + "@";
                    queryString = queryString.Substring(0, queryString.Length - 2);
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

                XElement xmlCol = prms.XmlTable.Descendants(queryString).FirstOrDefault();
                bool isInXml = xmlCol != null;

                paramName = Regex.Replace(paramName, @"[^\w]", "");

                if (!queryList.Contains(queryString) || !paramDict.ContainsKey("@" + paramName))
                {
                    queryList.Add(queryString);

                    List<string> conditionList = new List<string>();

                    for (int j = 0; j < paramValueArray.Length; j++)
                    {
                        conditionList.Add("[" + queryString + "]" + sign + paramName + signDict[sign] + j);
                    }

                    sqlBuilder.Append("(");
                    sqlBuilder.Append(String.Join(" OR ", conditionList));
                    sqlBuilder.Append(")");
                    sqlBuilder.Append(" AND ");
                }

                for (int j = 0; j < paramValueArray.Length; j++)
                {
                    if (isInXml && xmlCol.Attribute("percentageform") != null && xmlCol.Attribute("percentageform").Value == "true")
                    {
                        paramValueArray[j] = (Convert.ToDouble(paramValueArray[j]) / 100).ToString();
                    }

                    if (!paramDict.ContainsKey("@" + paramName + signDict[sign] + j))
                    {
                        if (isInXml && xmlCol.Attribute("encrypted") != null)
                        {
                            paramValueArray[j] = decrypt(paramValueArray[j]);
                        }

                        paramDict.Add("@" + paramName + signDict[sign] + j, paramValueArray[j]);
                    }
                    else
                    {
                        paramDict["@" + paramName + signDict[sign] + j] = paramValueArray[j];
                    }
                }
            }

            if (hasWhere && allKeys.Length > 0)
            {
                sqlBuilder.Remove(sqlBuilder.Length - 5, 5);
            }
        }

        static bool BeginWithNumber(string colName)
        {
            int k = 0;
            return Int32.TryParse(colName[0].ToString(), out k);
        }

        static void BuildRectBody(Params prms, DataTable dataTable, StringBuilder tableBuilder)
        {
            XElement xButton = prms.XmlTable.Descendants("buttons").FirstOrDefault();
            bool hasCheckbox = prms.Context.Request["hasCheckbox"] == "true";
            string checkedCol = string.Empty;

            if (prms.XmlTable.Attribute("checkbox") != null)
            {
                checkedCol = prms.XmlTable.Attribute("checkbox").Value;
            }
            else
            {
                checkedCol = prms.XmlTable.Descendants().First().Name.ToString();
            }

            tableBuilder.Append("<div class=\\\"rt-rectbody\\\">");

            foreach (DataRow row in dataTable.Rows)
            {
                tableBuilder.Append("<div class=\\\"rt-rectbody-block\\\">");

                if (hasCheckbox)
                {
                    tableBuilder.Append("<div class=\\\"rt-td-checkbox\\\" name=\\\"rt-td-checkbox\\\" data-value=\\\"" + row[dataTable.Columns[0]] + "\\\">");
                    tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");

                    if (!dataTable.Columns.Contains(checkedCol))
                    {
                        Exception excepion = new Exception("checkbox所指列不存在");
                        throw excepion;
                    }

                    tableBuilder.Append("<input type=\\\"checkbox\\\"  class=\\\"rt-checkbox\\\" value=\\\"" + row[checkedCol] + "\\\" />");
                    tableBuilder.Append("</div>");
                    tableBuilder.Append("</div>");
                }

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ToString();
                    ColumnConfig colConfig = prms.ColConfigDict[colName];

                    if (colConfig.Visibility == "none")
                    {
                        continue;
                    }

                    tableBuilder.Append("<div name=\\\"" + colName + "\\\" class=\\\"rt-rectbody-col");

                    if (colConfig.Visibility == "hidden")
                    {
                        tableBuilder.Append(" hiddenCol");
                    }

                    tableBuilder.Append("\\\"");

                    if (colConfig.HasLinkTo)
                    {
                        tableBuilder.Append(" data-table=\\\"" + colConfig.LinkTo + "\\\"");
                        tableBuilder.Append(" data-passedcol=\\\"");

                        for (int i = 0; i < colConfig.Passedcol.Count; i++)
                        {
                            tableBuilder.Append(colConfig.Passedcol[i] + "=" + HttpUtility.UrlEncode(row[colConfig.Passedcol[i]].ToString()));

                            if (i < colConfig.Passedcol.Count - 1)
                            {
                                tableBuilder.Append("&");
                            }
                        }

                        tableBuilder.Append("\\\"");

                        if (colConfig.HasNavname)
                        {
                            tableBuilder.Append(" data-navname=\\\"" + row[colConfig.Navname] + "\\\"");
                        }
                    }

                    string cellValue = row[col].ToString();

                    cellValue = Format(prms, row, colConfig, cellValue);

                    if (colName != "操作")
                    {
                        tableBuilder.Append(" data-value=\\\"" + cellValue + "\\\">");

                        if (colConfig.HasFormatter)
                        {
                            cellValue = FormatCell(dataTable.Columns, row, colConfig.Formatter, colName, cellValue);
                        }

                        tableBuilder.Append(cellValue);

                        if (colConfig.HasBtn)
                        {
                            tableBuilder.Append("<span class=\\\"rt-cell-btn glyphicon glyphicon-" + colConfig.BtnIcon + "\\\" onclick=\\\"" + colConfig.BtnFunc + "\\\"></span>");
                        }
                    }
                    else
                    {
                        tableBuilder.Append(">" + BuildButtonCell(prms.Context, row, xButton));
                    }

                    tableBuilder.Append("</div>");
                }

                tableBuilder.Append("</div>");
            }

            tableBuilder.Append("</div>");
        }

        static void BuildRectHead(Params prms, DataTable dataTable, StringBuilder tableBuilder, StringBuilder searchBuilder, List<string> sortList, int page, int totalPages)
        {
            tableBuilder.Append("<div class=\\\"rt-rectheader\\\">");
            tableBuilder.Append("<div class=\\\"rt-rectheader-sort\\\">");

            if (prms.Context.Request["hasCheckbox"] == "true")
            {
                tableBuilder.Append("<div class=\\\"rt-th-checkbox\\\" name=\\\"rt-th-checkbox\\\">");
                tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");
                tableBuilder.Append("<input type=\\\"checkbox\\\" class=\\\"rt-checkbox\\\"/>");
                tableBuilder.Append("</div>");
                tableBuilder.Append("</div>");
            }

            foreach (DataColumn col in dataTable.Columns)
            {
                XElement xmlCol = null;
                string colName = col.ToString();
                SetColConfig(colName, ref xmlCol, prms, dataTable);
                ColumnConfig colConfig = prms.ColConfigDict[colName];

                BuildSearchingBlock(prms.Context, xmlCol, colConfig, searchBuilder);

                if (colConfig.Visibility == "hidden" || colConfig.Visibility == "none" || colName == "操作")
                {
                    continue;
                }

                tableBuilder.Append("<div class=\\\"rt-sort\\\" name=\\\"" + colName + "\\\">");
                tableBuilder.Append(colConfig.Text);

                if (sortList.Any() && sortList[0] == colName)
                {
                    tableBuilder.Append("<span class=\\\"glyphicon glyphicon-");

                    if (sortList[1] == "ASC")
                    {
                        tableBuilder.Append("arrow-up");
                    }
                    else
                    {
                        tableBuilder.Append("arrow-down");
                    }

                    tableBuilder.Append("\\\"></span>");
                }

                tableBuilder.Append("</div>");

                if (colConfig.Search4Admin && !prms.IsAdmin)
                {
                    continue;
                }
            }

            tableBuilder.Append("</div>");

            tableBuilder.Append("<div class=\\\"rt-rectheader-pager\\\">");
            tableBuilder.Append("<span>" + page + "/" + totalPages + "</span>");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-backward rt-pager-prevPage\\\"></span>");
            tableBuilder.Append("<span class=\\\"pager-separator\\\"></span>");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-forward rt-pager-nextPage\\\"></span>");
            tableBuilder.Append("</div>");

            tableBuilder.Append("</div>");
        }

        static string BuildButtonCell(HttpContext context, DataRow row, XElement xButton)
        {
            var btns = xButton.Elements();
            List<XElement> btnList = new List<XElement>();

            foreach (XElement btn in btns)
            {
                if (!IsDisplayed(context, btn))
                {
                    continue;
                }

                var attributes = from n in btn.Attributes() where n != null && n.Value.Contains("$$:") select n;

                foreach (var attr in attributes)
                {
                    attr.Value = ReplacePlaceholder(row, attr.Value);
                }

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

                btnList.Add(btn);
            }

            return String.Join("", btnList).Replace("\"", "\\\"");
        }

        static void BuildColPager(StringBuilder tableBuilder, int colPage, int totalColPages)
        {
            tableBuilder.Append("<div class=\\\"rt-colPager-container\\\">");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-chevron-left rt-colPager-prev\\\"></span>");
            tableBuilder.Append("<input type=\\\"hidden\\\" class=\\\"rt-colPager-page\\\" value=\\\"" + colPage + "\\\"/>");
            tableBuilder.Append("<input type=\\\"hidden\\\" class=\\\"rt-colPager-totalColPages\\\" value=\\\"" + totalColPages + "\\\"/>");
            tableBuilder.Append("<span class=\\\"glyphicon glyphicon-chevron-right rt-colPager-next\\\"></span>");
            tableBuilder.Append("</div>");
        }

        static void BuildLocationsResult(HttpContext context, XDocument xml, string tableID, string condition, List<string> result)
        {
            XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == tableID select n).FirstOrDefault();
            Params prms = new Params(context, xmlTable, IsAdministrator(context, xmlTable));

            string table = GetTableName(prms);

            string connectionString = "Data Source=192.168.2.192;Initial Catalog=PLM3;Persist Security Info=True;User ID=sa;Password=112321";
            SqlConnection sqlCon = new SqlConnection(connectionString);
            SqlCommand sqlCmd = new SqlCommand();
            sqlCmd.Connection = sqlCon;

            if (xmlTable.Attribute("exec-before") != null)
            {
                ExecuteStoredProcedure(prms, sqlCon, sqlCmd);
            }

            Dictionary<string, string> paramDict = new Dictionary<string, string>();
            string conditionInParamForm = string.Empty;
            string[] conditionArray = condition.Split('&');

            foreach (string con in conditionArray)
            {
                string[] conditionPair = con.Split('=');
                paramDict.Add("@" + conditionPair[0], conditionPair[1]);
                conditionInParamForm += conditionPair[0] + "=@" + conditionPair[0];
            }

            string sql = "SELECT * FROM " + table + " WHERE " + conditionInParamForm;
            SqlDataAdapter dataAdapter = new SqlDataAdapter(sql, connectionString);

            SetParameters(dataAdapter, paramDict);

            DataTable dataTable = new DataTable();
            dataTable.PrimaryKey = null;
            dataAdapter.Fill(dataTable);

            StringBuilder bodyBuilder = new StringBuilder();
            StringBuilder searchBuilder = new StringBuilder();
            Dictionary<string, ColumnConfig> colConfigDict = new Dictionary<string, ColumnConfig>();
            List<string> sortList = new List<string>();

            foreach (DataColumn col in dataTable.Columns)
            {
                XElement xmlCol = null;
                string colName = col.ToString();
                SetColConfig(colName, ref xmlCol, prms, dataTable);
            }
            bodyBuilder = new StringBuilder();
            BuildTree(prms, dataTable, bodyBuilder);

            XAttribute parentNode = xmlTable.Attribute("parentnode");

            if (parentNode != null)
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> parentDict = (Dictionary<string, object>)serializer.DeserializeObject(parentNode.Value);
                Dictionary<string, object> relations = (Dictionary<string, object>)parentDict.Values.ElementAt(0);
                string parentID = parentDict.Keys.ElementAt(0);

                paramDict.Clear();
                conditionInParamForm = string.Empty;

                foreach (var rel in relations)
                {
                    paramDict.Add("@" + rel.Value, dataTable.Rows[0][rel.Key].ToString());
                    conditionInParamForm += rel.Value + "=@" + rel.Value;
                }

                XElement parentTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == parentID select n).FirstOrDefault();
                Params parentPrms = new Params(context, parentTable, IsAdministrator(context, parentTable));
                table = GetTableName(parentPrms);

                dataAdapter.SelectCommand.CommandText = "SELECT * FROM " + table + " WHERE " + conditionInParamForm;
                dataAdapter.SelectCommand.Parameters.Clear();
                SetParameters(dataAdapter, paramDict);
                dataTable.Clear();
                dataAdapter.Fill(dataTable);

                if (dataTable.Rows.Count == 0)
                {
                    result.Add("{\"parent\":\"ROOTNODE\",\"elems\":\"" + bodyBuilder.ToString() + "\"}");
                    return;
                }

                dynamic childTree = serializer.DeserializeObject(parentTable.Attribute("childtree").Value);
                List<string> childList = new List<string>();
                List<string> conditionList = new List<string>();

                foreach (var tree in childTree)
                {
                    string kvPair = string.Empty;
                    kvPair += "\\\"" + tree.Key + "\\\":\\\"";
                    foreach (var col in tree.Value)
                    {
                        conditionList.Add(col.Value + "=" + dataTable.Rows[0][col.Key]);
                    }

                    kvPair += String.Join("&", conditionList) + "\\\"";
                    childList.Add(kvPair);
                    conditionList.Clear();
                }

                result.Add("{\"parent\":\"" + String.Join(",", childList) + "\",\"elems\":\"" + bodyBuilder.ToString() + "\"}");

                XAttribute grandParentNode = parentTable.Attribute("parentnode");

                if (grandParentNode != null)
                {
                    Dictionary<string, object> grandParentDict = (Dictionary<string, object>)serializer.DeserializeObject(grandParentNode.Value);
                    Dictionary<string, object> grandRelations = (Dictionary<string, object>)grandParentDict.Values.ElementAt(0);

                    foreach (var rel in grandRelations)
                    {
                        conditionList.Add(rel.Key + "=" + dataTable.Rows[0][rel.Key] + "");
                    }

                    BuildLocationsResult(context, xml, parentID, String.Join(" AND ", conditionList), result);
                }
            }
        }

        static void BuildNullRow(Params prms, DataTable dataTable, StringBuilder rowBuilder)
        {
            XElement xButton = prms.XmlTable.Descendants("buttons").FirstOrDefault();
            XAttribute checkbox = prms.XmlTable.Attribute("checkbox");
            string checkedCol = string.Empty;

            if (checkbox != null)
            {
                checkedCol = checkbox.Value;
            }

            rowBuilder.Append("<tr>");

            if (checkbox != null)
            {
                rowBuilder.Append("<td class=\\\"rt-td-checkbox\\\" name=\\\"rt-td-checkbox\\\" data-value=\\\"\\\">");
                rowBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");
                rowBuilder.Append("<input type=\\\"checkbox\\\"  class=\\\"rt-checkbox\\\" value=\\\"\\\" />");
                rowBuilder.Append("</div>");
                rowBuilder.Append("</td>");
            }

            DataRow row = dataTable.NewRow();

            foreach (DataColumn col in dataTable.Columns)
            {
                string colName = col.ToString();
                ColumnConfig colConfig = prms.ColConfigDict[colName];

                rowBuilder.Append("<td name=\\\"" + colName + "\\\"");

                if (colConfig.Visibility == "hidden")
                {
                    rowBuilder.Append(" class=\\\"hiddenCol\\\"");
                }

                string cellValue = row[col].ToString();

                cellValue = Format(prms, row, colConfig, cellValue);

                if (colConfig.HasDefaultValue)
                {
                    cellValue = colConfig.DefaultValue;
                }

                if (colName != "操作")
                {
                    rowBuilder.Append(" data-value=\\\"" + cellValue + "\\\">");

                    if (colConfig.HasFormatter)
                    {
                        cellValue = FormatCell(dataTable.Columns, row, colConfig.Formatter, colName, cellValue);
                    }

                    rowBuilder.Append(cellValue);

                    if (colConfig.HasBtn)
                    {
                        rowBuilder.Append("<span class=\\\"rt-cell-btn glyphicon glyphicon-" + colConfig.BtnIcon + "\\\" onclick=\\\"" + colConfig.BtnFunc + "\\\"></span>");
                    }

                    rowBuilder.Append("</td>");
                }
                else
                {
                    rowBuilder.Append(">" + BuildButtonCell(prms.Context, row, xButton) + "</td>");
                }
            }

            rowBuilder.Append("</tr>");
        }

        static string BuildSelectorBar(Params prms, SqlDataAdapter dataAdapter, string sql, List<string> queryList, StringBuilder selectorBuilder, StringBuilder conditionBuilder)
        {
            foreach (var ccd in prms.ColConfigDict)
            {
                ColumnConfig colConfig = prms.ColConfigDict[ccd.Key];

                if (colConfig.IsInselector == false)
                {
                    continue;
                }

                Type cachedData = typeof(CachedData);
                DataTable dataTable = new DataTable();
                Dictionary<string, string> data = new Dictionary<string, string>();

                if (!String.IsNullOrWhiteSpace(colConfig.Selector))
                {
                    data = (Dictionary<string, string>)cachedData.GetProperty(colConfig.Selector).GetValue(null, BindingFlags.Default, null, null, null);
                }
                else
                {
                    dataAdapter.SelectCommand.CommandText = "SELECT DISTINCT  " + ccd.Key + " " + sql;
                    dataTable.PrimaryKey = null;
                    dataAdapter.Fill(dataTable);
                    MethodInfo formatter = cachedData.GetMethod(colConfig.SelectorFunc);
                    data = (Dictionary<string, string>)formatter.Invoke(null, new object[] { dataTable });
                }

                if (queryList.Contains(ccd.Key))
                {
                    string key = ccd.Key;
                    string queryValue = prms.Context.Request.QueryString[ccd.Key];

                    if (queryValue == null)
                    {
                        continue;
                    }

                    string[] originValue = queryValue.Split('|');
                    List<string> valueText = new List<string>();

                    foreach (var v in originValue)
                    {
                        if (!data.ContainsKey(v))
                        {
                            continue;
                        }
                        valueText.Add(data[v]);
                    }

                    if (!String.IsNullOrWhiteSpace(colConfig.SelectorText))
                    {
                        key = colConfig.SelectorText;
                    }
                    else if (!String.IsNullOrWhiteSpace(colConfig.Text))
                    {
                        key = colConfig.Text;
                    }

                    conditionBuilder.Append("<div data-value=\\\"" + ccd.Key + "\\\">");
                    conditionBuilder.Append(key + "：");

                    conditionBuilder.Append(String.Join("、", valueText));
                    conditionBuilder.Append("<span class=\\\"glyphicon glyphicon-remove rt-condition-remove\\\"></div>");
                    conditionBuilder.Append("</div>");

                    continue;
                }

                selectorBuilder.Append("<div class=\\\"rt-selector-folder\\\">");

                string selectorText = String.IsNullOrWhiteSpace(colConfig.SelectorText) ? colConfig.Text : colConfig.SelectorText;

                selectorBuilder.Append("<div class=\\\"rt-selector-key\\\" data-value=\\\"" + ccd.Key + "\\\">" + selectorText + "：</div>");

                selectorBuilder.Append("<div class=\\\"rt-selector-value\\\">");
                selectorBuilder.Append("<ul class=\\\"rt-selector-list\\\">");
                foreach (var d in data)
                {
                    selectorBuilder.Append("<li data-value=\\\"" + d.Key + "\\\"><span class=\\\"rt-selector-list-text\\\"><span class=\\\"glyphicon glyphicon-unchecked\\\"></span>" + d.Value + "</span></li>");
                }
                selectorBuilder.Append("</ul>");
                selectorBuilder.Append("</div>");

                if (colConfig.HasSelectorMulti)
                {
                    selectorBuilder.Append("<div class=\\\"rt-multiselect-btns\\\">");
                    selectorBuilder.Append("<button class=\\\"btn btn-primary btn-xs rt-multiselect-ok\\\">确&nbsp;&nbsp;定</button><button class=\\\"btn btn-default btn-xs rt-multiselect-cancel\\\">取&nbsp;&nbsp;消</button>");
                    selectorBuilder.Append("</div>");
                }

                selectorBuilder.Append("<div class=\\\"rt-selector-btns\\\">");
                selectorBuilder.Append("<span class=\\\"rt-selector-selectmore\\\"><span class=\\\"rt-selectmore-txt\\\">更多</span><span class=\\\"glyphicon glyphicon-chevron-down\\\"></span></span>");
                if (colConfig.HasSelectorMulti)
                {
                    selectorBuilder.Append("<span class=\\\"rt-selector-multiselect\\\">多选<span class=\\\"glyphicon glyphicon-plus\\\"></span></span>");
                }
                selectorBuilder.Append("</div>");

                selectorBuilder.Append("</div>");
            }

            return selectorBuilder.ToString();
        }

        static void BuildSearchingBlock(HttpContext context, XElement xmlCol, ColumnConfig colConfig, StringBuilder searchBuilder)
        {
            if (xmlCol == null || xmlCol.Attribute("search-type") == null)
            {
                return;
            }

            colConfig.HasSearchType = true;
            colConfig.SearchType = xmlCol.Attribute("search-type").Value;

            searchBuilder.Append("<div");

            if (xmlCol.Attribute("search-adv") != null && xmlCol.Attribute("search-adv").Value == "true")
            {
                searchBuilder.Append(" class=\\\"rt-search-adv\\\"");
            }

            searchBuilder.Append(">");
            searchBuilder.Append("<span class=\\\"rt-search-heading\\\">" + colConfig.Text + "：</span>");

            string searchType = xmlCol.Attribute("search-type").Value;

            if (searchType == "range" || searchType == "date")
            {
                searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt form-control " + searchType + "\\\" name=\\\"" + colConfig.ColumnName + "\\\" data-sign=\\\"%3e%3d\\\" value=\\\"" + context.Request.QueryString[colConfig.ColumnName + ">="] + "\\\"/>");
                searchBuilder.Append("<span class=\\\"search-span-minus\\\"> - </span>");
                searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt form-control " + searchType + "\\\" name=\\\"" + colConfig.ColumnName + "\\\" data-sign=\\\"%3c%3d\\\" value=\\\"" + context.Request.QueryString[colConfig.ColumnName + "<="] + "\\\"/>");
                searchBuilder.Append("</div>");
            }
            else
            {
                searchBuilder.Append("<input type=\\\"text\\\" class=\\\"rt-search-txt form-control");

                string value = string.Empty;

                if (!String.IsNullOrWhiteSpace(context.Request.QueryString[colConfig.ColumnName]))
                {
                    value = context.Request.QueryString[colConfig.ColumnName];
                }
                else
                {
                    value = context.Request.QueryString[colConfig.ColumnName + "~~"];
                }

                searchBuilder.Append("\\\" name=\\\"" + colConfig.ColumnName + "\\\" data-sign=\\\"%7e%7e\\\" value=\\\"" + value + "\\\"/>");

                if (xmlCol != null && xmlCol.Attribute("search-btn-icon") != null && xmlCol.Attribute("search-btn-func") != null)
                {
                    searchBuilder.Append("<span class=\\\"glyphicon glyphicon-" + xmlCol.Attribute("search-btn-icon").Value + " rt-search-txt-btn\\\" onclick=\\\"" + xmlCol.Attribute("search-btn-func").Value + "\\\"></span>");
                }

                searchBuilder.Append("</div>");
            }
        }

        static void BuildSearchingSql(HttpContext context, XDocument xml, string tableID, Dictionary<string, string> sqlDict)
        {
            List<string> conditionList = new List<string>();
            XElement xmlTable = (from n in xml.Descendants("table") where n.Attribute("id").Value == tableID select n).FirstOrDefault();
            Params prms = new Params(context, xmlTable, IsAdministrator(context, xmlTable));
            var visibleCols = from n in xmlTable.Elements() where n.Attribute("visibility") != null && n.Attribute("visibility").Value == "visible" select n;

            foreach (var col in visibleCols)
            {
                conditionList.Add("[" + col.Name + "]" + " LIKE @" + col.Name + "");
            }

            string table = GetTableName(prms);

            sqlDict.Add(tableID, "SELECT * FROM " + table + " WHERE " + String.Join(" OR ", conditionList));

            XAttribute childTree = xmlTable.Attribute("childtree");

            if (childTree != null)
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                var trees = (Dictionary<string, object>)serializer.DeserializeObject(xmlTable.Attribute("childtree").Value);
                foreach (var tree in trees)
                {
                    if (tableID != tree.Key)
                    {
                        BuildSearchingSql(context, xml, tree.Key, sqlDict);
                    }
                }
            }
        }

        static void BuildTableBody(Params prms, DataTable dataTable, StringBuilder tableBuilder)
        {
            XElement xButton = prms.XmlTable.Descendants("buttons").FirstOrDefault();
            bool hasCheckbox = prms.Context.Request["hasCheckbox"] == "true";
            string checkedCol = string.Empty;

            if (prms.XmlTable.Attribute("checkbox") != null)
            {
                checkedCol = prms.XmlTable.Attribute("checkbox").Value;
            }
            else
            {
                checkedCol = prms.XmlTable.Descendants().First().Name.ToString();
            }

            tableBuilder.Append("<tbody>");

            foreach (DataRow row in dataTable.Rows)
            {
                tableBuilder.Append("<tr>");

                if (hasCheckbox)
                {
                    tableBuilder.Append("<td class=\\\"rt-td-checkbox\\\" name=\\\"rt-td-checkbox\\\" data-value=\\\"" + row[dataTable.Columns[0]] + "\\\">");
                    tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");

                    if (!dataTable.Columns.Contains(checkedCol))
                    {
                        Exception excepion = new Exception("checkbox所指列不存在");
                        throw excepion;
                    }

                    tableBuilder.Append("<input type=\\\"checkbox\\\"  class=\\\"rt-checkbox\\\" value=\\\"" + row[checkedCol] + "\\\" />");
                    tableBuilder.Append("</div>");
                    tableBuilder.Append("</td>");
                }

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ToString();
                    ColumnConfig colConfig = prms.ColConfigDict[colName];

                    if (colConfig.Visibility == "none")
                    {
                        continue;
                    }

                    tableBuilder.Append("<td name=\\\"" + colName + "\\\"");

                    if (colConfig.Visibility == "hidden")
                    {
                        tableBuilder.Append(" class=\\\"hiddenCol\\\"");
                    }

                    if (colConfig.HasLinkTo)
                    {
                        tableBuilder.Append(" data-table=\\\"" + colConfig.LinkTo + "\\\"");
                        tableBuilder.Append(" data-passedcol=\\\"");

                        for (int i = 0; i < colConfig.Passedcol.Count; i++)
                        {
                            tableBuilder.Append(colConfig.Passedcol[i] + "=" + HttpUtility.UrlEncode(row[colConfig.Passedcol[i]].ToString()));

                            if (i < colConfig.Passedcol.Count - 1)
                            {
                                tableBuilder.Append("&");
                            }
                        }

                        tableBuilder.Append("\\\"");

                        if (colConfig.HasNavname)
                        {
                            tableBuilder.Append(" data-navname=\\\"" + row[colConfig.Navname] + "\\\"");
                        }
                    }

                    string cellValue = row[col].ToString();

                    cellValue = Format(prms, row, colConfig, cellValue);

                    if (colName != "操作")
                    {
                        tableBuilder.Append(" data-value=\\\"" + cellValue + "\\\">");

                        if (colConfig.HasFormatter)
                        {
                            cellValue = FormatCell(dataTable.Columns, row, colConfig.Formatter, colName, cellValue);
                        }

                        tableBuilder.Append(cellValue);

                        if (colConfig.HasBtn)
                        {
                            tableBuilder.Append("<span class=\\\"rt-cell-btn glyphicon glyphicon-" + colConfig.BtnIcon + "\\\" onclick=\\\"" + colConfig.BtnFunc + "\\\"></span>");
                        }
                    }
                    else
                    {
                        tableBuilder.Append(">" + BuildButtonCell(prms.Context, row, xButton));
                    }

                    tableBuilder.Append("</td>");
                }

                tableBuilder.Append("</tr>");
            }

            tableBuilder.Append("</tbody>");
        }

        static void BuildTableHead(Params prms, DataTable dataTable, StringBuilder tableBuilder, StringBuilder searchBuilder, List<string> sortList)
        {
            tableBuilder.Append("<thead>");
            tableBuilder.Append("<tr>");

            if (prms.Context.Request["hasCheckbox"] == "true")
            {
                tableBuilder.Append("<th class=\\\"rt-th-checkbox\\\" name=\\\"rt-th-checkbox\\\">");
                tableBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");
                tableBuilder.Append("<input type=\\\"checkbox\\\" class=\\\"rt-checkbox\\\"/>");
                tableBuilder.Append("</div>");
                tableBuilder.Append("</th>");
            }

            foreach (DataColumn col in dataTable.Columns)
            {
                XElement xmlCol = null;
                string colName = col.ToString();
                SetColConfig(colName, ref xmlCol, prms, dataTable);
                ColumnConfig colConfig = prms.ColConfigDict[colName];

                if (colConfig.Visibility == "none")
                {
                    continue;
                }

                BuildSearchingBlock(prms.Context, xmlCol, colConfig, searchBuilder);

                tableBuilder.Append("<th");

                if (colName != "操作")
                {
                    tableBuilder.Append(" class=\\\"");

                    if (colConfig.Visibility == "hidden")
                    {
                        tableBuilder.Append("hiddenCol");
                    }
                    else
                    {
                        tableBuilder.Append("rt-sort");

                    }

                    tableBuilder.Append("\\\"");
                }

                tableBuilder.Append(" name=\\\"" + colName + "\\\">");
                tableBuilder.Append(colConfig.Text);

                if (sortList.Any() && sortList[0] == colName)
                {
                    tableBuilder.Append("<span class=\\\"glyphicon glyphicon-");

                    if (sortList[1] == "ASC")
                    {
                        tableBuilder.Append("arrow-up");
                    }
                    else
                    {
                        tableBuilder.Append("arrow-down");
                    }

                    tableBuilder.Append("\\\"></span>");
                }

                tableBuilder.Append("</th>");
            }

            tableBuilder.Append("</tr>");
            tableBuilder.Append("</thead>");
        }

        static void BuildTablePager(Params prms, StringBuilder tableBuilder, string[] rowList, int page, int rows, int totalRecords, int totalPages, string style)
        {
            tableBuilder.Append("<div class=\\\"rt-pager-container\\\">");

            tableBuilder.Append("<div class=\\\"rt-pager-buttons\\\">");

            if (prms.XmlTable.Attribute("search") == null || prms.XmlTable.Attribute("search").Value != "false")
            {
                //tableBuilder.Append("<span class=\\\"rt-pager-search rt-pager-btn\\\"><span class=\\\"glyphicon glyphicon-search\\\" title=\\\"查询\\\"></span>查询</span>");
            }
            if (prms.XmlTable.Attribute("excel") != null && prms.XmlTable.Attribute("excel").Value == "true")
            {
                tableBuilder.Append("<span class=\\\" rt-pager-export rt-pager-btn\\\"><span class=\\\"glyphicon glyphicon-export\\\" title=\\\"导出Excel\\\"></span>导出</span>");
            }

            XElement pagerButtons = prms.XmlTable.Descendants("pagerbuttons").FirstOrDefault();
            if (pagerButtons != null)
            {
                var btns = pagerButtons.Nodes();
                foreach (XElement btn in btns)
                {
                    if (!IsDisplayed(prms.Context, btn))
                    {
                        continue;
                    }

                    var btnStr = btn.ToString().Replace("\"", "\\\"");
                    var elemStr = Regex.Replace(Regex.Replace(btnStr, @"\s+<", @"<"), @">\s+", @">");
                    tableBuilder.Append(elemStr);
                }
            }

            tableBuilder.Append("</div>"); //rt-pager-buttons

            if (style != Style.Tree)
            {
                tableBuilder.Append("<div class=\\\"rt-pager-controls\\\">");
                tableBuilder.Append("&nbsp;<span class=\\\"glyphicon glyphicon-step-backward rt-pager-firstPage\\\"></span>");
                tableBuilder.Append("&nbsp;<span class=\\\"glyphicon glyphicon-backward rt-pager-prevPage\\\"></span>");
                tableBuilder.Append("&nbsp;<span class=\\\"pager-separator\\\"></span>&nbsp;");
                tableBuilder.Append("第&nbsp;<input type=\\\"text\\\" class=\\\"rt-pager-page\\\" value=\\\"" + page + "\\\"/>&nbsp;页，");
                tableBuilder.Append("共&nbsp;<span class=\\\"rt-pager-totalPages\\\">" + totalPages + "</span>&nbsp;页");
                tableBuilder.Append("&nbsp;<span class=\\\"pager-separator\\\"></span>&nbsp;");
                tableBuilder.Append("<span class=\\\"glyphicon glyphicon-forward rt-pager-nextPage\\\"></span>&nbsp;");
                tableBuilder.Append("<span class=\\\"glyphicon glyphicon-step-forward rt-pager-lastPage\\\"></span>&nbsp;&nbsp;");
                tableBuilder.Append("<select class=\\\"rt-pager-rowList\\\">");

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
                tableBuilder.Append("</div>"); //rt-pager-controls

                tableBuilder.Append("<div class=\\\"rt-pager-records\\\">第&nbsp;" + ((page - 1) * rows + 1) + " - " + ((page * rows) <= totalRecords ? (page * rows) : totalRecords) + "&nbsp;条，");
                tableBuilder.Append("共&nbsp;<span class=\\\"rt-pager-totalRecords\\\">" + totalRecords + "</span>&nbsp;条</div>");
            }

            tableBuilder.Append("</div>");
        }

        static void BuildTree(Params prms, DataTable dataTable, StringBuilder bodyBuilder)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            XElement xButton = prms.XmlTable.Descendants("buttons").FirstOrDefault();
            string tableID = prms.XmlTable.Attribute("id").Value;
            string checkedCol = string.Empty;
            bool hasCheckbox = prms.Context.Request["hasCheckbox"] == "true";
            bool hasButtons = dataTable.Columns.Contains("操作");
            bool hasChildTree = false;
            bool hasParentNode = false;
            dynamic childTree = null;
            dynamic parentNode = null;

            if (prms.XmlTable.Attribute("checkbox") != null)
            {
                checkedCol = prms.XmlTable.Attribute("checkbox").Value;
            }
            else
            {
                checkedCol = prms.XmlTable.Descendants().First().Name.ToString();
            }

            if (prms.XmlTable.Attribute("childtree") != null)
            {
                hasChildTree = true;
                childTree = serializer.DeserializeObject(prms.XmlTable.Attribute("childtree").Value);
            }
            if (prms.XmlTable.Attribute("parentnode") != null)
            {
                hasParentNode = true;
                parentNode = serializer.DeserializeObject(prms.XmlTable.Attribute("parentnode").Value);
            }

            foreach (DataRow row in dataTable.Rows)
            {
                bodyBuilder.Append("<div class=\\\"rt-node\\\">");
                bodyBuilder.Append("<span class=\\\"rt-node-line\\\"></span>");

                if (hasChildTree)
                {
                    bodyBuilder.Append("<span class=\\\"glyphicon glyphicon-triangle-right\\\"></span>");
                }
                else
                {
                    bodyBuilder.Append("<span class=\\\"rt-node-extline\\\"></span>");
                    bodyBuilder.Append("<span class=\\\"glyphicon glyphicon-blank\\\"></span>");
                }

                if (hasCheckbox)
                {
                    bodyBuilder.Append("<div class=\\\"rt-checkboxWrapper\\\">");

                    if (!dataTable.Columns.Contains(checkedCol))
                    {
                        Exception excepion = new Exception("checkbox所指列不存在");
                        throw excepion;
                    }

                    bodyBuilder.Append("<input type=\\\"checkbox\\\" class=\\\"rt-checkbox\\\" value=\\\"" + row[checkedCol] + "\\\" />");
                    bodyBuilder.Append("</div>");
                }

                bodyBuilder.Append("<div class=\\\"rt-node-cols\\\" data-tableid=\\\"" + tableID + "\\\"");

                if (hasChildTree)
                {
                    bodyBuilder.Append(" data-childtree='{");
                    List<string> treeList = new List<string>();

                    foreach (var tree in childTree)
                    {
                        string kvPair = string.Empty;
                        kvPair += "\\\"" + tree.Key + "\\\":\\\"";
                        List<string> conditionList = new List<string>();

                        foreach (var col in tree.Value)
                        {
                            conditionList.Add(col.Value + "=" + row[col.Key]);
                        }

                        kvPair += String.Join("&", conditionList) + "\\\"";
                        treeList.Add(kvPair);
                    }

                    bodyBuilder.Append(String.Join(",", treeList) + "}'");
                }

                if (hasParentNode)
                {
                    bodyBuilder.Append(" data-parentnode=\\\"");

                    foreach (var node in parentNode)
                    {
                        List<string> conditionList = new List<string>();

                        foreach (var col in node.Value)
                        {
                            conditionList.Add(col.Key + "=" + row[col.Key]);
                        }

                        bodyBuilder.Append(String.Join("&", conditionList) + "\\\"");
                    }
                }

                bodyBuilder.Append(">");

                foreach (DataColumn col in dataTable.Columns)
                {
                    string colName = col.ToString();
                    ColumnConfig colConfig = prms.ColConfigDict[colName];
                    string cellValue = row[col].ToString();

                    if (colConfig.Visibility == "visible")
                    {
                        bodyBuilder.Append("<div name=\\\"" + colName + "\\\"");
                        cellValue = Format(prms, row, colConfig, cellValue);
                        bodyBuilder.Append(" data-value=\\\"" + cellValue + "\\\">");
                    }
                    else if (colConfig.Visibility == "hidden")
                    {
                        bodyBuilder.Append("<input type=\\\"hidden\\\" name=\\\"" + colName + "\\\"");
                        cellValue = Format(prms, row, colConfig, cellValue);
                        bodyBuilder.Append(" data-value=\\\"" + cellValue + "\\\">");
                        continue;
                    }
                    else
                    {
                        continue;
                    }

                    if (colConfig.HasFormatter)
                    {
                        cellValue = FormatCell(dataTable.Columns, row, colConfig.Formatter, colName, cellValue);
                    }

                    bodyBuilder.Append(cellValue);

                    if (colConfig.HasBtn)
                    {
                        bodyBuilder.Append("<span class=\\\"rt-cell-btn glyphicon glyphicon-" + colConfig.BtnIcon + "\\\" onclick=\\\"" + colConfig.BtnFunc + "\\\"></span>");
                    }

                    bodyBuilder.Append("</div>");
                }

                bodyBuilder.Append("</div>");

                if (hasButtons)
                {
                    bodyBuilder.Append("<div class=\\\"rt-node-btns\\\" name=\\\"操作\\\">");
                    bodyBuilder.Append(BuildButtonCell(prms.Context, row, xButton));
                    bodyBuilder.Append("</div>");
                }

                bodyBuilder.Append("</div>");
            }
        }

        static void CountRecords(SqlConnection sqlCon, SqlCommand sqlCmd, StringBuilder sqlBuilder, Dictionary<string, string> paramDict, int rows, ref int totalRecords, ref int totalPages)
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

        static void ExecuteStoredProcedure(Params prms, SqlConnection sqlCon, SqlCommand sqlCmd)
        {
            string[] storedProcedures = prms.XmlTable.Attribute("exec-before").Value.Split(';');

            try
            {
                foreach (var sp in storedProcedures)
                {
                    int index = sp.IndexOf("(");
                    string spStr = "exec " + sp.Substring(0, index) + " ";
                    string[] parameters = sp.Substring(index + 1, sp.Length - index - 2).Split(',');

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        spStr += "'" + prms.Context.Request.QueryString[parameters[i]] + "'";

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

        static void ExportExcel(Params prms, DataTable dataTable, SqlDataAdapter dataAdapter, StringBuilder sqlBuilder, Dictionary<string, string> paramDict, MemoryStream excelStream)
        {
            foreach (DataColumn col in dataTable.Columns)
            {
                XElement xmlCol = null;
                string colName = col.ToString();
                SetColConfig(colName, ref xmlCol, prms, dataTable);
            }

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

                if (!BeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
                {
                    xmlCol = prms.XmlTable.Descendants(colName).FirstOrDefault();
                }
                else
                {
                    xmlCol = prms.XmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
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

                    if (!BeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
                    {
                        xmlCol = prms.XmlTable.Descendants(colName).FirstOrDefault();
                    }
                    else
                    {
                        xmlCol = prms.XmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
                    }

                    string cellValue = dataTable.Rows[i][j].ToString();
                    cellValue = Format(prms, dataTable.Rows[i], prms.ColConfigDict[colName], cellValue);

                    row.CreateCell(j).SetCellValue(cellValue);
                }
            }

            workbook.Write(excelStream);
        }

        static void FillTable(HttpContext context, XElement xmlTable)
        {

        }

        static string Format(Params prms, DataRow row, ColumnConfig colConfig, string cellValue)
        {
            string formattedCell = string.Empty;
            bool isEmpty = String.IsNullOrWhiteSpace(cellValue);

            if (!isEmpty && colConfig.HasDateformat)
            {
                formattedCell = Convert.ToDateTime(cellValue).ToString(colConfig.Dateformat);
            }
            else if (!isEmpty && colConfig.HasPrecision)
            {
                double val;

                if (Double.TryParse(cellValue, out val))
                {
                    bool percentageform = colConfig.IsInPercentageform;

                    if (percentageform)
                    {
                        val = val * 100;
                    }

                    formattedCell = val.ToString(colConfig.Precision);

                    if (percentageform)
                    {
                        formattedCell += "%";
                    }

                    if (formattedCell == "-100.00%")
                    {
                        formattedCell = "0.00%";
                    }
                }
                else
                {
                    formattedCell = cellValue;
                }
            }
            else if (!isEmpty && colConfig.HasTimetransfer)
            {
                switch (colConfig.Timetransfer)
                {
                    case "second":
                        int val;
                        if (int.TryParse(cellValue, out val))
                        {
                            formattedCell = (val / (24 * 60 * 60)).ToString() + "日" + ((val - (val / (24 * 60 * 60)) * 60 * 60 * 24) / 3600).ToString()
                                               + "时" + ((val - (val / (60 * 60)) * 60 * 60) / 60).ToString() + "分" + (val - (val / 60) * 60).ToString() + "秒";
                        }
                        else
                        {
                            formattedCell = cellValue;
                        }
                        break;
                    default:
                        formattedCell = cellValue;
                        break;
                }
            }
            else
            {
                formattedCell = cellValue;
            }

            if (colConfig.HasRegex)
            {
                formattedCell = Regex.Replace(formattedCell, colConfig.RegexPattern, colConfig.RegexReplacement);
            }

            if (colConfig.HasFormatterR)
            {
                formattedCell = ReplacePlaceholder(row, colConfig.FormatterR, formattedCell);
            }

            return formattedCell;
        }

        static string FormatCell(DataColumnCollection columns, DataRow row, string formatterName, string colName, string cellValue)
        {
            string formattedCell = cellValue;

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

            return formattedCell;
        }

        static string GetTableName(Params prms)
        {
            if (prms.IsAdmin && prms.XmlTable.Attribute("adminname") != null)
            {
                return prms.XmlTable.Attribute("adminname").Value;
            }
            else
            {
                return prms.XmlTable.Attribute("name").Value;
            }
        }

        static bool IsAdministrator(HttpContext context, XElement xmlTable)
        {
            if (context.Session == null || context.Session["identitystate"] == null)
            {
                return false;
            }

            string identity = context.Session["identitystate"].ToString();
            string staffNo = context.Session["Uname"].ToString();

            XAttribute role = xmlTable.Attribute("admin-role");
            XAttribute power = xmlTable.Attribute("admin-power");

            if (role == null && power == null)
            {
                return false;
            }

            List<string> roleList = new List<string>();
            List<string> powerList = new List<string>();

            if (role != null)
            {
                roleList = role.Value.Split(',').ToList();
            }
            if (power != null)
            {
                powerList = power.Value.Split(',').ToList();
            }

            if (roleList.Contains(identity) || powerList.Contains(staffNo))
            {
                return true;
            }

            foreach (var p in powerList)
            {
                if (IMSROOT.App_Code.SysUserBase.GetPowerForCheck(p))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsDisplayed(HttpContext context, XElement xBtn)
        {
            bool isDisplayed = false;

            bool hasLoggedIn = context.Session != null && context.Session["identitystate"] != null;

            if (xBtn.Attribute("display-role") == null && xBtn.Attribute("display-power") == null)
            {
                isDisplayed = true;
            }

            if (xBtn.Attribute("display-role") != null)
            {
                if (hasLoggedIn)
                {
                    List<string> roleList = xBtn.Attribute("display-role").Value.Split(',').ToList();
                    string identity = context.Session["identitystate"].ToString();

                    if (roleList.Contains(identity))
                    {
                        isDisplayed = true;
                    }
                }
            }

            if (xBtn.Attribute("display-power") != null)
            {
                List<string> powerList = new List<string>();
                powerList = xBtn.Attribute("display-power").Value.Split(',').ToList();

                if (hasLoggedIn)
                {
                    foreach (var p in powerList)
                    {
                        if (IMSROOT.App_Code.SysUserBase.GetPowerForCheck(p))
                        {
                            isDisplayed = true;
                        }
                    }
                }
            }

            return isDisplayed;
        }

        static string ReplacePlaceholder(DataRow row, string inStr, string value = "")
        {
            string outStr = inStr;

            for (int i = inStr.IndexOf("$$:"), j = inStr.IndexOf(":$$"); i > -1; i = inStr.IndexOf("$$:", i + 1), j = inStr.IndexOf(":$$", j + 1))
            {
                int startPos = i + 3;
                int endPos = j;
                string colName = inStr.Substring(startPos, endPos - startPos);
                if (String.IsNullOrEmpty(colName))
                {
                    if (value == "")
                    {
                        outStr = outStr.Replace("$$::$$", "");
                    }
                    else
                    {
                        outStr = outStr.Replace("$$::$$", value);
                    }
                }
                else
                {
                    string colValue = row[colName] == null ? string.Empty : row[colName].ToString();
                    outStr = outStr.Replace("$$:" + colName + ":$$", colValue);
                }
            }

            return outStr;
        }

        static void SetColConfig(string colName, ref XElement xmlCol, Params prms, DataTable dataTable)
        {
            if (!BeginWithNumber(colName)) //xml元素不许以数字开头，数字开头列需（"bwn" + colName）
            {
                xmlCol = prms.XmlTable.Descendants(colName).FirstOrDefault();
            }
            else
            {
                xmlCol = prms.XmlTable.Descendants("bwn" + colName).FirstOrDefault(); ;
            }

            bool isInXml = xmlCol != null;

            prms.ColConfigDict.Add(colName, new ColumnConfig());
            ColumnConfig colConfig = prms.ColConfigDict[colName];
            colConfig.ColumnName = colName;
            colConfig.Text = colName;

            if (isInXml)
            {
                if (xmlCol.Attribute("btn-icon") != null && xmlCol.Attribute("btn-func") != null)
                {
                    colConfig.HasBtn = true;
                    colConfig.BtnIcon = xmlCol.Attribute("btn-icon").Value;
                    colConfig.BtnFunc = xmlCol.Attribute("btn-func").Value;
                }

                if (xmlCol.Attribute("dateformat") != null)
                {
                    colConfig.HasDateformat = true;
                    colConfig.Dateformat = xmlCol.Attribute("dateformat").Value;
                }

                if (xmlCol.Attribute("defaultvalue") != null)
                {
                    colConfig.HasDefaultValue = true;
                    colConfig.DefaultValue = xmlCol.Attribute("defaultvalue").Value;
                }

                if (xmlCol.Attribute("formatter") != null)
                {
                    colConfig.HasFormatter = true;
                    colConfig.Formatter = xmlCol.Attribute("formatter").Value;
                }

                if (xmlCol.Attribute("formatter-r") != null)
                {
                    colConfig.HasFormatterR = true;
                    colConfig.FormatterR = xmlCol.Attribute("formatter-r").Value;
                }

                if (xmlCol.Attribute("selector") != null)
                {
                    colConfig.IsInselector = true;
                    colConfig.Selector = xmlCol.Attribute("selector").Value;
                }
                else if (xmlCol.Attribute("selector-func") != null)
                {
                    colConfig.IsInselector = true;
                    colConfig.SelectorFunc = xmlCol.Attribute("selector-func").Value;
                }

                if (xmlCol.Attribute("selector-text") != null)
                {
                    colConfig.SelectorText = xmlCol.Attribute("selector-text").Value;
                }

                if (xmlCol.Attribute("linkto") != null && xmlCol.Attribute("passedcol") != null)
                {
                    colConfig.HasLinkTo = true;
                    colConfig.LinkTo = xmlCol.Attribute("linkto").Value;
                    colConfig.Passedcol = xmlCol.Attribute("passedcol").Value.Split(',').ToList();

                    if (prms.IsAdmin && xmlCol.Attribute("ignoredpassedcol") != null)
                    {
                        string[] ignoredpassedcol = xmlCol.Attribute("ignoredpassedcol").Value.Split(',');
                        foreach (var ip in ignoredpassedcol)
                        {
                            colConfig.Passedcol.Remove(ip);
                        }
                    }
                }

                if (xmlCol.Attribute("selector-multi") != null && xmlCol.Attribute("selector-multi").Value == "true")
                {
                    colConfig.HasSelectorMulti = true;
                }

                if (xmlCol.Attribute("navname") != null)
                {
                    colConfig.HasNavname = true;
                    string navname = xmlCol.Attribute("navname").Value;
                    if (!dataTable.Columns.Contains(navname))
                    {
                        Exception excepion = new Exception("navname所指列不存在");
                        throw excepion;
                    }
                    colConfig.Navname = navname;
                }

                if (xmlCol.Attribute("search4admin") != null && xmlCol.Attribute("search4admin").Value == "true")
                {
                    colConfig.Search4Admin = true;
                }

                if (xmlCol.Attribute("text") != null)
                {
                    colConfig.Text = xmlCol.Attribute("text").Value;
                }

                if (xmlCol.Attribute("timetransfer") != null)
                {
                    colConfig.HasTimetransfer = true;
                    colConfig.Timetransfer = xmlCol.Attribute("timetransfer").Value;
                }

                if (xmlCol.Attribute("precision") != null)
                {
                    colConfig.HasPrecision = true;
                    colConfig.Precision = xmlCol.Attribute("precision").Value;
                }

                if (xmlCol.Attribute("percentageform") != null && xmlCol.Attribute("percentageform").Value == "true")
                {
                    colConfig.IsInPercentageform = true;
                }

                if (xmlCol.Attribute("regex-pattern") != null && xmlCol.Attribute("regex-replacement") != null)
                {
                    colConfig.HasRegex = true;
                    colConfig.RegexPattern = xmlCol.Attribute("regex-pattern").Value;
                    colConfig.RegexReplacement = xmlCol.Attribute("regex-replacement").Value;
                }

                if (xmlCol.Attribute("visibility") != null)
                {
                    colConfig.Visibility = xmlCol.Attribute("visibility").Value;
                }
            }
        }

        static void SetParameters(SqlDataAdapter dataAdapter, Dictionary<string, string> paramDict)
        {
            foreach (var pair in paramDict)
            {
                dataAdapter.SelectCommand.Parameters.Add(new SqlParameter
                {
                    ParameterName = pair.Key,
                    Value = pair.Value,
                });
            }
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

            encryptedText = encryptedText.Replace(" ", "+");
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

        class Params
        {
            HttpContext context;
            XElement xmlTable;
            bool isAdmin;

            public Dictionary<string, ColumnConfig> ColConfigDict { get; set; }
            public HttpContext Context
            {
                get { return context; }
            }
            public XElement XmlTable
            {
                get { return xmlTable; }
            }
            public bool IsAdmin
            {
                get { return isAdmin; }
            }

            public Params(HttpContext context, XElement xmlTable, bool isAdmin)
            {
                ColConfigDict = new Dictionary<string, ColumnConfig>();
                this.context = context;
                this.xmlTable = xmlTable;
                this.isAdmin = isAdmin;
            }
        }

        class ColumnConfig
        {
            public bool HasBtn { get; set; }
            public bool HasDateformat { get; set; }
            public bool HasDefaultValue { get; set; }
            public bool HasFormatter { get; set; }
            public bool HasFormatterR { get; set; }
            public bool HasLinkTo { get; set; }
            public bool HasNavname { get; set; }
            public bool HasPrecision { get; set; }
            public bool HasRegex { get; set; }
            public bool HasSearchType { get; set; }
            public bool HasSelectorMulti { get; set; }
            public bool HasTimetransfer { get; set; }
            public bool IsInPercentageform { get; set; }
            public bool IsInselector { get; set; }
            public bool Search4Admin { get; set; }
            public string BtnIcon { get; set; }
            public string BtnFunc { get; set; }
            public string ColumnName { get; set; }
            public string Dateformat { get; set; }
            public string DefaultValue { get; set; }
            public string Formatter { get; set; }
            public string FormatterR { get; set; }
            public string LinkTo { get; set; }
            public string Navname { get; set; }
            public string Timetransfer { get; set; }
            public string Precision { get; set; }
            public string RegexPattern { get; set; }
            public string RegexReplacement { get; set; }
            public string SearchType { get; set; }
            public string Selector { get; set; }
            public string SelectorFunc { get; set; }
            public string SelectorText { get; set; }
            public string Text { get; set; }
            public string Visibility { get; set; } //"visible", "hidden"
            public List<string> Passedcol { get; set; }

            public ColumnConfig()
            {
                Passedcol = null;
            }
        }

        struct Style
        {
            public const string Rect = "rect";
            public const string Table = "table";
            public const string Tree = "tree";
        }
    }
}