
using HDCore.TCP.Server;
using HDImportXml.Core;
using Ionic.Zip;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HDTCP.Util {

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }
        public const int BUF_SIZE = 4096;

        #region HTTP
        
        public override void handleGETRequest (HttpProcessor p)
		{

            if (p.http_url.Equals("/offset"))
            {
                p.writeSuccess();
                p.outputStreamWriter.WriteLine(this.OffsetFtp(p).ToString());
                return;
			}
            handleDownloadRequest(p);
            return;

            Console.WriteLine("request: {0}", p.http_url);
            p.writeSuccess();
            p.outputStreamWriter.WriteLine("<html><body><h1>Not supported method</h1>");
        }

        public override void handlePOSTRequest(HttpProcessor p) {
            Console.WriteLine("POST request: {0}", p.http_url);
            MemoryStream ms = new MemoryStream();

            if (p.httpHeaders.ContainsKey("Content-Length"))
            {
                int content_len = Convert.ToInt32(p.httpHeaders["Content-Length"]);
                if (content_len > HttpProcessor.MAX_POST_SIZE)
                {
                    throw new Exception(
                        String.Format("POST Content-Length({0}) too big for this simple server",
                          content_len));
                }
                byte[] buf = new byte[BUF_SIZE];
                int to_read = content_len;
                while (to_read > 0)
                {
                    Console.WriteLine("starting Read, to_read={0}", to_read);

                    int numread = p.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                    Console.WriteLine("read finished, numread={0}", numread);
                    if (numread == 0)
                    {
                        if (to_read == 0)
                        {
                            break;
                        }
                        else
                        {
                            throw new Exception("client disconnected during post");
                        }
                    }
                    to_read -= numread;
                    ms.Write(buf, 0, numread);
                }
                ms.Seek(0, SeekOrigin.Begin);
            }
            StreamReader inputData = new StreamReader(ms);
            string data = inputData.ReadToEnd();
             
            p.writeSuccess();
            p.outputStreamWriter.WriteLine("<html><body><h1>test server</h1>");
            p.outputStreamWriter.WriteLine("<a href=/test>return</a><p>");
            p.outputStreamWriter.WriteLine("postbody: <pre>{0}</pre>", data);

        }

        public override void handleUploadRequest(HttpProcessor p)
        {
            try
            {
                Console.WriteLine(": Start transfer file");

                
                long total = StoreLocal(p);
                Console.WriteLine(": Completed transfer file. "+total);
                p.writeSuccess();
                p.outputStreamWriter.WriteLine("{\"Status\":\"Success\",\"Size\":" + total + "}");
            }
            catch (Exception ex)
            {
                p.writeSuccess();
                p.outputStreamWriter.WriteLine("{\"Status\":\"Failed\",\"Message\":\"" + ex.Message + "\"}");
            }
        }

        public override void handleDownloadRequest(HttpProcessor p)
        {
            DownloadLocal(p);
        }

        public long StoreLocal(HttpProcessor p)
        {
            long total = 0;
            try
            {
                String fileName = p.httpHeaders["X-File-Name"].ToString();
                String fullpath = "C:/" + fileName;
                Int64 startOffset = Convert.ToInt64(p.httpHeaders["X-Start-Offset"]);
                Int64 contenSize = Convert.ToInt64(p.httpHeaders["X-File-Size"]);
                FileStream ms = null;
                byte[] buf = new byte[BUF_SIZE];
                Console.WriteLine(": Start transfer file. Start-Offset: " + startOffset);
                if (startOffset > 0)
                {
                    ms = new FileStream("C:/" + fileName, FileMode.Append, FileAccess.Write);
                }
                else
                {
                    ms = new FileStream("C:/" + fileName, FileMode.Create, FileAccess.Write);
                }
               
                //ms.Seek(startOffset, SeekOrigin.Begin);
                int numread = p.inputStream.Read(buf, 0, (int)Math.Min(BUF_SIZE, contenSize));
                while (numread > 0 && contenSize > 0)
                {
                    ms.Write(buf, 0, numread);
                    total += numread;
                    contenSize -= numread;
                    numread = p.inputStream.Read(buf, 0, (int)Math.Min(BUF_SIZE, contenSize));
                }
                ms.Flush();
                ms.Close();
            }
            catch (Exception)
            {
                Console.WriteLine("!!!Error: You do not have permission to acess this folder!");
            }
            return total;
        }

        public long StoreFtp(HttpProcessor p)
        {
            String fileName = p.httpHeaders["X-File-Name"].ToString();
            FTPConnection ftp = new FTPConnection("127.0.0.1", "c", "c");
            Int64 startOffset = Convert.ToInt64(p.httpHeaders["X-Start-Offset"]);
            Int64 contenSize = Convert.ToInt64(p.httpHeaders["X-File-Size"]);
            ftp.OpenUploadStream(fileName, startOffset>0);
            long total = 0;
            try
            {
                total = ftp.UploadStream(p.inputStream, contenSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error" + ex);
            }
            ftp.Disconnect();
            return total;
        }

        public long OffsetFtp(HttpProcessor p)
        {
            String fileName = p.httpHeaders["X-File-Name"].ToString();
            FTPConnection ftp = new FTPConnection("127.0.0.1", "c", "c");
            long size = ftp.GetFileSize(fileName);
            ftp.Disconnect();
            return size;
        }

        public long OffsetLocal(HttpProcessor p)
        {
            String fileName = p.httpHeaders["X-File-Name"].ToString();
            FileInfo fi = new FileInfo("C:/" + fileName);
            long size = fi.Exists? fi.Length:0;
            return size;
        }

        public void DownloadLocal(HttpProcessor p)
        {
            try
            {
                p.writeSuccess("application/octet-stream");
                p.outputStreamWriter.WriteLine("Content-Disposition: attachment;filename=\"test.mxf\"");
                Console.WriteLine(": Start transfer file");
                long total = 0;
                //String fileName = p.httpHeaders["X-File-Name"].ToString();
                FileInfo fi = new FileInfo("C:/0003QS.MXF");
                if (!fi.Exists)
                {
                    throw new Exception("File not found");
                }
                long contentSize = fi.Length;
                using (ZipFile zip = new ZipFile())
                {
                    zip.UseZip64WhenSaving = Zip64Option.Always;
                    zip.AlternateEncodingUsage = ZipOption.AsNecessary;
                    ZipEntry files = zip.AddDirectoryByName("Files");
                    zip.AddEntry("Files/0002D000.MXF", new FileStream("C:/0002D000.MXF", FileMode.Open, FileAccess.Read));
                    zip.AddEntry("Files/0002D002.MXF", new FileStream("C:/0002D000.MXF", FileMode.Open, FileAccess.Read));
                    zip.Save(p.outputStream);
                }
                //FileStream stream = new FileStream("C:/0003QS.MXF", FileMode.Open, FileAccess.Read);
                //byte[] buf = new byte[BUF_SIZE];
                //int numread = stream.Read(buf, 0,BUF_SIZE);
                //while (numread > 0 && contentSize>0)
                //{
                //    p.outputStream.Write(buf, 0, numread);
                //    total += numread;
                //    contentSize -= numread;
                //    numread = stream.Read(buf, 0, (int)Math.Min(contentSize,BUF_SIZE));
                //}

                Console.WriteLine(": Completed transfer file. " + total);
                //p.outputStreamWriter.WriteLine("{\"Status\":\"Success\",\"Size\":" + total + "}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(": Error transfer file. ");
                p.writeSuccess();
                p.outputStreamWriter.WriteLine("{\"Status\":\"Failed\",\"Message\":\"" + ex.Message + "\"}");
            }
        }

        #endregion

        #region WS

        public override void handleWSRequest(WsProcessor p)
        {
            int lastOpcode = 0;
            long total = 0;
            int nbSend = 0;
            while (true)
            {
                while (!p.stream.DataAvailable){ Thread.Sleep(100); }

                Byte[] bytes = new Byte[p.socket.Available];

                p.stream.Read(bytes, 0, bytes.Length);

                int len = bytes[1] - 128;
                int offset = 2;
                if (len == 126)
                {
                    len = 0;
                    for (int i = 0; i < 2; i++)
                        len += ((int)bytes[offset + i] << (int)((1 - i) * 8));
                    offset += 2;
                }
                else if (len == 127)
                {
                    len = 0;
                    for (int i = 0; i < 8; i++)
                        len += ((int)bytes[offset + i] << ((7 - i) * 8));
                    offset += 8;
                }

                if (bytes.Length - offset - 4 == len)
                {
                    Byte[] key = new Byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
                    offset += 4;

                    int FIN = (bytes[0] >> 7) & 0x01;
                    int RSV1 = (bytes[0] >> 6) & 0x01;
                    int RSV2 = (bytes[0] >> 5) & 0x01;
                    int RSV3 = (bytes[0] >> 4) & 0x01;
                    int opcode = bytes[0] & 0x0f;

                    if (RSV1 != 0 || RSV2 != 0 || RSV3 != 0)
                    {
                        Console.WriteLine("Error RSV");
                        p.socket.Close();
                        break;
                    }

                    if (opcode == 0)
                        opcode = lastOpcode;

                    Byte[] decoded = new Byte[len];

                    for (int i = 0; i < len; i++)
                    {
                        decoded[i] = (Byte)(bytes[i + offset] ^ key[i % 4]);
                    }

                    if (opcode == 8)
                    {
                        Console.WriteLine("Client " + p.ThreadId + " send close");
                        p.socket.Close();
                        break;
                    }

                    switch (opcode)
                    {
                        // Text frame
                        case 1:
                            string revice = Encoding.UTF8.GetString(decoded);
                            Console.WriteLine("Client " + p.ThreadId + " send text: " + revice);
                            SendToAll(revice, p.ThreadId);
                            break;

                        case 2:
                            nbSend++;
                            total += decoded.Length;
                            //Console.WriteLine("Client send binary " + decoded.Length + " bytes, total " + total + " bytes, send " + nbSend);
                            break;

                        case 9:
                            Console.WriteLine("Client " + p.ThreadId + " send a ping");
                            break;

                        case 10:
                            Console.WriteLine("Client " + p.ThreadId + " send a pong");
                            break;

                        default:
                            Console.WriteLine("Client " + p.ThreadId + " send Further control or no-control frame");
                            break;
                    }
                }
            }
            this.wsProcessors.Remove(p);
        }

        public void SendToAll(string mss, int expectedId=0)
        {
            foreach(var p in this.wsProcessors){
                if (expectedId > 0 && p.ThreadId == expectedId)
                {
                    continue;
                }
                p.Send(mss);
            }
        }
        #endregion

    }

    public class TestMain {
        public static int Main(String[] args) {
            HttpServer httpServer;
            if (args.GetLength(0) > 0)
            {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            }
            else
            {
                httpServer = new MyHttpServer(8081);
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }
    }

}
