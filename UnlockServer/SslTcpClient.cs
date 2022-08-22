using System;
using System.Collections;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Threading;

namespace UnlockServer
{
    public class SslTcpClient
    {
        private static Hashtable certificateErrors = new Hashtable();
        // The following method is invoked by the RemoteCertificateValidationDelegate.
        public static bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;
            return true;
        }
        public static bool RunClient(string machineName,int port,string data)
        {
            try
            { 
                TcpClient client = new TcpClient(machineName, port);
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;

                SslStream sslStream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                sslStream.ReadTimeout = 10000;
                sslStream.WriteTimeout = 10000;

                try
                {
                    sslStream.AuthenticateAsClient("Localhost");
                }
                catch (AuthenticationException e)
                { 
                    client.Close();
                    return false;
                } 
                byte[] messsage = Encoding.UTF8.GetBytes(data); 
                sslStream.Write(messsage);
                sslStream.Flush();
                 
                byte[] buffer = new byte[1024];
                  
                var ret = new List<byte>();
                int a = 1;
                while (a != 0)
                { 
                    a = sslStream.Read(buffer, 0, 1024);
                    if(a>0)
                    {
                        ret.AddRange(buffer.Take(a).ToList());
                    } 
                } 
                var retstr = Encoding.UTF8.GetString(ret.ToArray());
              
                sslStream.Close();
                client.Close();
                Thread.Sleep(2000);
                return WanClient.IsSessionLocked() == false;
            }catch(Exception ex)
            {
                return false;
            }
        } 
    }
}