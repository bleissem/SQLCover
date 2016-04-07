﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using SQLCover.Gateway;

namespace SQLCover.Trace
{
    internal class TraceController
    {
        
        private const string CreateTrace = @"CREATE EVENT SESSION [{0}] ON SERVER 
ADD EVENT sqlserver.sp_statement_starting(action (sqlserver.plan_handle, sqlserver.tsql_stack) where ([sqlserver].[database_id]=({1})))
ADD TARGET package0.asynchronous_file_target(
     SET filename='{2}')
WITH (MAX_MEMORY=100 MB,EVENT_RETENTION_MODE=NO_EVENT_LOSS,MAX_DISPATCH_LATENCY=1 SECONDS,MAX_EVENT_SIZE=0 KB,MEMORY_PARTITION_MODE=NONE,TRACK_CAUSALITY=OFF,STARTUP_STATE=OFF) 
";

        private const string StartTraceFormat = @"alter event session [{0}] on server state = start
";

        private const string StopTraceFormat = @"alter event session [{0}] on server state = stop
";

        private const string DropTraceFormat = @"drop EVENT SESSION [{0}] ON SERVER ";

        private const string ReadTraceFormat = @"select
    object_name,
    event_data,
    file_name,
    file_offset
FROM sys.fn_xe_file_target_read_file(N'{0}*.xel', N'{0}*.xem', null, null);";

        private const string GetLogDir = @"EXEC xp_readerrorlog 0, 1, N'Logging SQL Server messages in file'";
        private readonly string _databaseId;
        private readonly DatabaseGateway _gateway;
        private string _fileName;

        private readonly string _name;

        public TraceController(DatabaseGateway gateway, string databaseName)
        {
            _gateway = gateway;
            _databaseId = gateway.GetString(string.Format("select db_id('{0}')", databaseName));
            _name = string.Format("SQLCover-Trace-{0}", Guid.NewGuid().ToString().Replace("{", "").Replace("}", "").Replace("-", ""));
        }

        private void Create()
        {
            var logDir = _gateway.GetRecords(GetLogDir).Rows[0].ItemArray[2].ToString();
            if (string.IsNullOrEmpty(logDir))
            {
                throw new InvalidOperationException("Unable to use xp_readerrorlog to find log directory to write extended event file");
            }

            logDir = logDir.ToUpper().Replace("Logging SQL Server messages in file '".ToUpper(), "").Replace("'", "").Replace("ERRORLOG.", "").Replace("ERROR.LOG", "");
            _fileName = Path.Combine(logDir, _name);

            RunScript(CreateTrace, "Error creating the extended events trace, error: {0}");
        }

        public void Start()
        {
            Create();
            RunScript(StartTraceFormat, "Error starting the extended events trace, error: {0}");
        }

        public void Stop()
        {
            RunScript(StopTraceFormat, "Error stopping the extended events trace, error: {0}");
        }

        public List<string> ReadTrace()
        {
            var data = _gateway.GetRecords(string.Format(ReadTraceFormat, _fileName));
            var events = new List<string>();
            foreach (DataRow row in data.Rows)
            {
                events.Add(row.ItemArray[1].ToString());
            }


            return events;
        }

        public void Drop()
        {
            RunScript(DropTraceFormat, "Error dropping the extended events trace, error: {0}");
            try
            {
                foreach (var file in new DirectoryInfo(new FileInfo(_fileName).DirectoryName).EnumerateFiles(new FileInfo(_fileName).Name + "*.*"))
                {
                    File.Delete(file.FullName);
                }
            }
            catch (Exception)
            {
            }
        }

        private void RunScript(string query, string error)
        {
            var script = GetScript(query);
            try
            {
                _gateway.Execute(script);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format(error, ex.Message), ex);
            }
        }

        private string GetScript(string query)
        {
            if (query.Contains("{2}"))
            {
                return string.Format(query, _name, _databaseId, _fileName + ".xel");
            }

            if (query.Contains("{1}"))
            {
                return string.Format(query, _name, _databaseId);
            }

            return string.Format(query, _name);
        }
    }
}