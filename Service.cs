using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using System.Collections.Specialized;
using System.Web;
using System.Data.Odbc;
using System.Configuration;


namespace QSTicketingWindowsService
{
    public partial class Service : ServiceBase
    {
        public Service()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {

            try {
                System.IO.Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
                HttpServer httpServer;
                httpServer = new MyHttpServer(Convert.ToInt32(ConfigurationManager.AppSettings["port"]));
                Thread thread = new Thread(new ThreadStart(httpServer.listen));
                thread.Start();
                Helper.WriteErrorLog("Service successfully started.");
            }
            catch (Exception e)
            {
            Helper.WriteErrorLog(e.ToString());
            }
        }

        protected override void OnStop()
        {
            
        }
    }

    public class HttpProcessor
    {
        public TcpClient socket;
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv)
        {
            this.socket = s;
            this.srv = srv;

        }


        private string streamReadLine(Stream inputStream)
        {
            int next_char;
            string data = "";
            while (true)
            {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }
            return data;
        }
        public void process()
        {
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try
            {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET"))
                {
                    handleGETRequest();
                }
                else if (http_method.Equals("POST"))
                {
                    handlePOSTRequest();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
                Helper.WriteErrorLog(e.ToString());
                //   writeFailure();
            }
            outputStream.Flush();
            // bs.Flush(); // flush any remaining output
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();
        }

        public void parseRequest()
        {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3)
            {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders()
        {
            Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null)
            {
                if (line.Equals(""))
                {
                    Console.WriteLine("got headers");
                    return;
                }

                int separator = line.IndexOf(':');
                if (separator == -1)
                {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' '))
                {
                    pos++; // strip any spaces
                }

                string value = line.Substring(pos, line.Length - pos);
                Console.WriteLine("header: {0}:{1}", name, value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest()
        {
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest()
        {
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length"))
            {
                content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                if (content_len > MAX_POST_SIZE)
                {
                    throw new Exception(
                        String.Format("POST Content-Length({0}) too big for this simple server",
                          content_len));
                }
                byte[] buf = new byte[BUF_SIZE];
                int to_read = content_len;
                while (to_read > 0)
                {
                    int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
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
            srv.handlePOSTRequest(this, new StreamReader(ms), httpHeaders);

        }

        public void writeSuccess(string content_type = "text/html")
        {
            outputStream.WriteLine("HTTP/1.0 200 OK");
            outputStream.WriteLine("Content-Type: " + content_type);
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }

        public void writeFailure()
        {
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }

        public void redirect(string url)
        {
          //  System.Web.HttpContext.Current.Response.Redirect(url);
        }

    }

    public abstract class HttpServer
    {

        protected int port;
        TcpListener listener;
        bool is_active = true;

        public HttpServer(int port)
        {
            this.port = port;
        }

        public void listen()
        {
            IPAddress localip = IPAddress.Parse("127.0.0.1");
            listener = new TcpListener(localip, port);
            listener.Start();
            Console.WriteLine("Ticketing service just started and now listening ...");
            while (is_active)
            {
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData, Hashtable httpHeaders);
    }

    public class MyHttpServer : HttpServer
    {
        public MyHttpServer(int port)
            : base(port)
        {
        }
        public override void handleGETRequest(HttpProcessor p)
        {

            if (p.http_url.EndsWith(".png"))
            {
                Stream fs = File.Open(p.http_url.Substring(1, p.http_url.Length - 1), FileMode.Open);
                //	p.writeSuccess("image/png");
                fs.CopyTo(p.outputStream.BaseStream);
                p.outputStream.BaseStream.Flush();
                fs.Close();
            }


            else if (p.http_url.EndsWith(".js") || p.http_url.EndsWith(".css"))
            {

                Stream fs = File.Open(p.http_url.Substring(1, p.http_url.Length - 1), FileMode.Open);
                fs.CopyTo(p.outputStream.BaseStream);
                p.outputStream.BaseStream.Flush();
                fs.Close();
            }


            if (p.http_url.StartsWith("/login"))
            {
                Stream fs = File.Open("login.htm", FileMode.Open);
                //    p.writeSuccess();

                fs.CopyTo(p.outputStream.BaseStream);
                p.outputStream.BaseStream.Flush();
                fs.Close();
            }


            // NameValueCollection querydata = HttpUtility.ParseQueryString("proxyRestUri=https%3a%2f%2fismail-nb.bistratejik.local%3a4243%2fqps%2fdotnet%2f&targetId=f7864213-cec5-4add-ba82-73f02285599d");
            // string proxyRestUri = querydata["proxyRestUri"];



        }

        public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData, Hashtable httpHeaders)
        {
            Console.WriteLine("POST request: {0}", p.http_url);
            string data = inputData.ReadToEnd();
            NameValueCollection qscoll = HttpUtility.ParseQueryString(data);
            var username = qscoll["username"];
            var password = qscoll["password"];
            var directory = ConfigurationManager.AppSettings["directory"];
            var host = ConfigurationManager.AppSettings["host"];
            var prefix = ConfigurationManager.AppSettings["prefix"];

            // username password kontolü burada yapılıyor.
            if (validateUser(username, password) == 1)
            {

                Helper.WriteErrorLog(username + password + directory + host + prefix);

                // ticket üretilip gönderiliyor.
                QlikSenseTicketRequest ticketexample = new QlikSenseTicketRequest();
                string ticketresponse = ticketexample.TicketRequest("POST", host, username, directory, prefix);
                Helper.WriteErrorLog(ticketresponse);
                Result list = JsonConvert.DeserializeObject<Result>(ticketresponse);
                list.Host = host;
                list.Prefix = prefix;
                string output = JsonConvert.SerializeObject(list);
                p.outputStream.WriteLine(output);


            }
            else
            {
                string noauthenticate = "{\"nouser\":\"1\"}";
                p.outputStream.WriteLine(noauthenticate);

            }
        }



        public int validateUser(string username, string password)
        {

          //  OleDbConnection con;
            OdbcConnection con;
          //  OleDbCommand com;
            OdbcCommand com;
            var connectionstring = ConfigurationManager.AppSettings["connectionstring"];
            var usertable = ConfigurationManager.AppSettings["usertablename"];
         //   con = new OleDbConnection(connectionstring);
            con = new OdbcConnection(connectionstring);
            string query = "Select count(*) as result from "+usertable+" where userid=? and pass=?";
        //    com = new OleDbCommand(query, con);
            com = new OdbcCommand(query, con);
            com.Parameters.AddWithValue("@p1", username);
            com.Parameters.AddWithValue("@p2", password);
            con.Open();
            int rowCount = (int)com.ExecuteScalar();
            con.Close();
            return rowCount;
        }



    }

    public class Result
    {
        public string Ticket { get; set; }
        public string TargetUri { get; set; }
        public string Host { get; set; }
        public string Prefix { get; set; }

    }


    public class NoResult
    {
        public string nouser { get; set; }
    }

}
