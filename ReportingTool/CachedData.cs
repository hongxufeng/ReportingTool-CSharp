using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using LinqToSql.Orm;

namespace IMSROOT.ReportingTool
{
    public class CachedData
    {
        private static Dictionary<string, string> imsroomName;

        public static Dictionary<string, string> ImsroomName
        {
            get
            {
                if (imsroomName == null)
                {
                    DataClassesDataContext db = new DataClassesDataContext();
                    var result = from n in db.t_imsrooms select new { code = n.name, name = n.name };
                    imsroomName = new Dictionary<string, string>();
                    foreach (var r in result)
                    {
                        imsroomName.Add(r.code, r.name);
                    }
                }

                return imsroomName;
            }
            set { imsroomName = value; }
        }

        public static Dictionary<string, string> GetValue(DataTable dataTable)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            foreach (DataRow dr in dataTable.Rows)
            {
                string value = dr[0].ToString();

                if (String.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result.Add(value, value);
            }

            return result;
        }
    }
}