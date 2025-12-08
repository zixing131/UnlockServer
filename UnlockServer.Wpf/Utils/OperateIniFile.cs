using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UnlockServer
{
    /// <summary>
    /// INI 配置文件操作类
    /// </summary>
    public class OperateIniFile
    {
        private static string _iniFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unlockserver.ini");

        public static string IniFileName
        {
            get { return _iniFileName; }
        }

        #region API函数声明

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32")]
        private static extern long GetPrivateProfileString(string section, string key, string def, StringBuilder retVal,
            int size, string filePath);

        #endregion

        public static string ReadIni(string section, string key, string noText = "")
        {
            return ReadIniData(section, key, noText, IniFileName);
        }

        public static float ReadIniFloat(string section, string key, float noText)
        {
            var t = ReadIniData(section, key, noText.ToString(), IniFileName);
            float o = 0;
            if (float.TryParse(t, out o))
            {
                return o;
            }
            return noText;
        }

        public static bool WriteIniFloat(string section, string key, float value)
        {
            return WriteIniData(section, key, value.ToString(), IniFileName);
        }

        public static int ReadIniInt(string section, string key, int noText)
        {
            var t = ReadIniData(section, key, noText.ToString(), IniFileName);
            int o = 0;
            if (int.TryParse(t, out o))
            {
                return o;
            }
            return noText;
        }

        public static string ReadIniString(string section, string key, string noText)
        {
            var t = ReadIniData(section, key, noText, IniFileName);
            return t;
        }

        public static string ReadSafeString(string section, string key, string noText)
        {
            var t = ReadIniData(section, key, "", IniFileName);
            if (string.IsNullOrEmpty(t))
            {
                return noText;
            }
            return AESUtil.AESDecrypt(t);
        }

        public static bool WriteIniInt(string section, string key, int value)
        {
            return WriteIniData(section, key, value.ToString(), IniFileName);
        }

        public static bool WriteIniString(string section, string key, string value)
        {
            return WriteIniData(section, key, value, IniFileName);
        }

        public static bool WriteSafeString(string section, string key, string value)
        {
            return WriteIniData(section, key, AESUtil.AESEncrypt(value), IniFileName);
        }

        #region 读Ini文件

        public static string ReadIniData(string section, string key, string noText, string iniFilePath)
        {
            if (iniFilePath.IndexOf('\\') == -1)
            {
                iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iniFilePath);
            }

            if (!File.Exists(iniFilePath))
            {
                File.CreateText(iniFilePath).Close();
            }

            StringBuilder temp = new StringBuilder(1024);

            GetPrivateProfileString(section, key, noText, temp, 1024, iniFilePath);

            return temp.ToString();
        }

        #endregion

        #region 写Ini文件

        public static bool WriteIniData(string section, string key, string value, string iniFilePath)
        {
            if (iniFilePath.IndexOf('\\') == -1)
            {
                iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iniFilePath);
            }

            if (!File.Exists(iniFilePath))
            {
                File.CreateText(iniFilePath).Close();
            }

            var opStation = WritePrivateProfileString(section, key, value, iniFilePath);

            return opStation != 0;
        }

        #endregion
    }
}

