using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace UnlockServer
{
    public class AESUtil
    {
        /// <summary>
        /// 获取密钥 必须是32字节
        /// </summary>
        private static byte[] Key = null;
        private static byte[] BaseKey = new byte[32] { 1, 2, 5, 4, 1, 2, 5, 9, 7, 1, 5, 1, 4, 1, 2, 1, 4, 5, 1, 4, 7, 8, 5, 2, 1, 4, 5, 7, 8, 5, 1, 2};

        /// <summary>
        /// AES加密
        /// </summary>
        /// <param name="plainStr">明文字符串</param>
        /// <returns>密文</returns>
        public static string AESEncrypt(string encryptStr,byte[] key = null)
        {
            if(key == null)
            {
                key = Key;
            }
            if(key == null)
            {
                initKey();
                key = Key;
            }
            byte[] keyArray =key;
            byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(encryptStr);
            RijndaelManaged rDel = new RijndaelManaged();
            rDel.Key = keyArray;
            rDel.Mode = CipherMode.ECB;
            rDel.Padding = PaddingMode.PKCS7;
            ICryptoTransform cTransform = rDel.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        }

        private static Random random = new Random();
        private static void initKey()
        {
            try
            {
                var key = OperateIniFile.ReadIniString("setting", "base", "");
                if (string.IsNullOrEmpty(key))
                {
                    Key = new byte[32];
                    random.NextBytes(Key);
                    var data = Convert.ToBase64String(Key);
                    OperateIniFile.WriteIniString("setting", "base", AESEncrypt(data, BaseKey));
                }
                else
                {
                    Key = Convert.FromBase64String(AESDecrypt(key, BaseKey));
                }
            }
            catch(Exception ex)
            {

            }
        }

        public static string AESDecrypt(string encryptStr, byte[] key=null)
        {
            if (key == null)
            {
                key = Key;
            }
            if (key == null)
            {
                initKey();
                key = Key;
            }
            byte[] keyArray = key;

            byte[] toEncryptArray = Convert.FromBase64String(encryptStr);
            RijndaelManaged rDel = new RijndaelManaged();
            rDel.Key = keyArray;
            rDel.Mode = CipherMode.ECB;
            rDel.Padding = PaddingMode.PKCS7;
            ICryptoTransform cTransform = rDel.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return UTF8Encoding.UTF8.GetString(resultArray);
        }
    }
}
