using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskScheduler;

namespace UnlockServer
{
    public class AutoStartHelper
    {
        public static bool AddStart()
        {
            try
            {
                //新建任务
                TaskSchedulerClass scheduler = new TaskSchedulerClass();
                //连接
                scheduler.Connect(null, null, null, null);
                //获取创建任务的目录
                ITaskFolder folder = scheduler.GetFolder("\\");
                //设置参数
                ITaskDefinition task = scheduler.NewTask(0);
                task.RegistrationInfo.Author = "zixing";//创建者
                task.RegistrationInfo.Description = "UnlockServer后台自启服务";//描述 
                task.Triggers.Create(_TASK_TRIGGER_TYPE2.TASK_TRIGGER_LOGON);
                //设置动作（此处为运行exe程序）
                IExecAction action = (IExecAction)task.Actions.Create(_TASK_ACTION_TYPE.TASK_ACTION_EXEC);
                action.Path = Process.GetCurrentProcess().MainModule.FileName;//设置文件目录
                action.Arguments = "hide";
                task.Settings.ExecutionTimeLimit = "PT0S"; //运行任务时间超时停止任务吗? PTOS 不开启超时
                task.Settings.DisallowStartIfOnBatteries = false;//只有在交流电源下才执行
                task.Settings.RunOnlyIfIdle = false;//仅当计算机空闲下才执行

                IRegisteredTask regTask =
                    folder.RegisterTaskDefinition("UnlockServer", task,//此处需要设置任务的名称（name）
                    (int)_TASK_CREATION.TASK_CREATE, null, //user
                    null, // password
                    _TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN,
                    "");
                IRunningTask runTask = regTask.Run(null); 
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

        }

        /// delete task
        /// </summary>
        /// <param name="taskName"></param>
        private static void DeleteTask(string taskName)
        {
            TaskSchedulerClass ts = new TaskSchedulerClass();
            ts.Connect(null, null, null, null);
            ITaskFolder folder = ts.GetFolder("\\");
            folder.DeleteTask(taskName, 0);
        } 
        public static bool RemoveStart()
        {
            try
            {
                DeleteTask("UnlockServer");
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }

        }
         
        /// <summary>
        /// check task isexists
        /// </summary>
        /// <param name="taskName"></param>
        /// <returns></returns>
        public static bool IsExists(string taskName= "UnlockServer")
        {
            var isExists = false;
            try
            {
                IRegisteredTaskCollection tasks_exists = GetAllTasks();
                for (int i = 1; i <= tasks_exists.Count; i++)
                {
                    IRegisteredTask t = tasks_exists[i];
                    if (t.Name.Equals(taskName))
                    {
                        isExists = true;
                        break;
                    }
                }
            }catch(Exception ex)
            {

            }
            return isExists;
        }

        /// <summary>
        /// get all tasks
        /// </summary>
        public static IRegisteredTaskCollection GetAllTasks()
        {
            TaskSchedulerClass ts = new TaskSchedulerClass();
            ts.Connect(null, null, null, null);
            ITaskFolder folder = ts.GetFolder("\\");
            IRegisteredTaskCollection tasks_exists = folder.GetTasks(1);
            return tasks_exists;
        }

    }
}
