using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace UnlockServer
{
    /// <summary>
    /// SSL TCP 客户端
    /// </summary>
    public class SslTcpClient
    {
        /// <summary>
        /// 验证服务器证书（允许所有证书）
        /// </summary>
        public static bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        /// <summary>
        /// 发送数据到服务器
        /// </summary>
        public static bool RunClient(string machineName, int port, string data)
        {
            try
            {
                TcpClient client = new TcpClient(machineName, port);
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;

                SslStream sslStream = new SslStream(
                    client.GetStream(),
                    false,
                    new RemoteCertificateValidationCallback(ValidateServerCertificate),
                    null);

                sslStream.ReadTimeout = 10000;
                sslStream.WriteTimeout = 10000;

                try
                {
                    sslStream.AuthenticateAsClient("Localhost");
                }
                catch (AuthenticationException e)
                {
                    LogHelper.WriteLine($"SSL认证失败: {e.Message}");
                    client.Close();
                    return false;
                }

                byte[] message = Encoding.UTF8.GetBytes(data);
                sslStream.Write(message);
                sslStream.Flush();

                byte[] buffer = new byte[1024];
                var ret = new List<byte>();
                int bytesRead = 1;

                while (bytesRead != 0)
                {
                    bytesRead = sslStream.Read(buffer, 0, 1024);
                    if (bytesRead > 0)
                    {
                        ret.AddRange(buffer.Take(bytesRead).ToList());
                    }
                }

                var retstr = Encoding.UTF8.GetString(ret.ToArray());
                LogHelper.WriteLine($"服务器响应: {retstr}");

                sslStream.Close();
                client.Close();

                Thread.Sleep(2000);
                return WanClient.IsSessionLocked() == false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"SSL通信失败: {ex.Message}");
                return false;
            }
        }
    }
}

