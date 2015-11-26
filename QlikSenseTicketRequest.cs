using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace QSTicketingWindowsService
{
    class QlikSenseTicketRequest
    {
        		private X509Certificate2 certificate_ { get; set; }

        public QlikSenseTicketRequest()
		{
			// First locate the Qlik Sense certificate
			X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
			store.Open(OpenFlags.ReadOnly);
			certificate_ = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.FriendlyName == "QlikClient");
			store.Close();
			ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
		}

		public string TicketRequest(string method, string server, string user, string userdirectory, string prefix)
		{
			//Create URL to REST endpoint for tickets
            string url = "https://" + server + ":4243/qps/" + prefix + "/ticket";

			//Create the HTTP Request and add required headers and content in Xrfkey
			string Xrfkey = "0123456789abcdef";
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url + "?Xrfkey=" + Xrfkey);
			// Add the method to authentication the user
			request.ClientCertificates.Add(new X509Certificate2("client.pfx", "1" /*,X509KeyStorageFlags.MachineKeySet*/));
        //    request.ClientCertificates.Add(certificate_);
			request.Method = method;
			request.Accept = "application/json";
			request.Headers.Add("X-Qlik-Xrfkey", Xrfkey);
            string body = "{ 'UserId':'" + user + "','UserDirectory':'" + userdirectory + "','Attributes': []}";
			byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

			if (!string.IsNullOrEmpty(body))
			{
				request.ContentType = "application/json";
				request.ContentLength = bodyBytes.Length;
				Stream requestStream = request.GetRequestStream();
				requestStream.Write(bodyBytes, 0, bodyBytes.Length);
				requestStream.Close();
			}

			// make the web request and return the content
			HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			Stream stream = response.GetResponseStream();
			return stream != null ? new StreamReader(stream).ReadToEnd() : string.Empty;
		}
    }
}
