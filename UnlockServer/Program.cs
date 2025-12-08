using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UnlockServer
{
    internal static class Program
    {
        public static bool ishideRun = false;


        // 防止程序二重启动互斥变量
        public static Mutex mutex;

        /// <summary>
        /// 检测是否单例
        /// </summary>
        /// <param name="flag"></param>
        /// <returns>是否只有一个单例在运行</returns>
        public static bool checkIsSingle(string flag = "UnlockServer_2022")
        {
            // 防止程序二重启动
            bool retValue = false; 
            mutex = new Mutex(true, flag, out retValue);
            return retValue; 
        }

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        { 
            ishideRun = args.Contains("hide");
            if (checkIsSingle()==false)
            {
                LogHelper.WriteLine("软件已经启动！");
                if(ishideRun ==false)
                {
                    MessageBox.Show("软件已经启动！");
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
