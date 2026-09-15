using System;
using System.Diagnostics;
using TaskScheduler;

namespace UnlockServer
{
    /// <summary>
    /// 开机自启助手类
    /// </summary>
    public class AutoStartHelper
    {
        private const string TaskName = "UnlockServer";

        /// <summary>
        /// 添加开机自启
        /// </summary>
        public static bool AddStart()
        {
            try
            {
                TaskSchedulerClass scheduler = new TaskSchedulerClass();
                scheduler.Connect(null, null, null, null);
                ITaskFolder folder = scheduler.GetFolder("\\");

                ITaskDefinition task = scheduler.NewTask(0);
                task.RegistrationInfo.Author = "zixing";
                task.RegistrationInfo.Description = "UnlockServer后台自启服务";

                task.Principal.RunLevel = _TASK_RUNLEVEL.TASK_RUNLEVEL_HIGHEST;
                task.Principal.LogonType = _TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN;

                var logonTrigger = (ILogonTrigger)task.Triggers.Create(_TASK_TRIGGER_TYPE2.TASK_TRIGGER_LOGON);
                logonTrigger.Delay = "PT15S";

                IExecAction action = (IExecAction)task.Actions.Create(_TASK_ACTION_TYPE.TASK_ACTION_EXEC);
                action.Path = Process.GetCurrentProcess().MainModule.FileName;
                action.Arguments = "hide";

                task.Settings.ExecutionTimeLimit = "PT0S";
                task.Settings.DisallowStartIfOnBatteries = false;
                task.Settings.StopIfGoingOnBatteries = false;
                task.Settings.RunOnlyIfIdle = false;
                task.Settings.AllowDemandStart = true;
                task.Settings.StartWhenAvailable = true;
                task.Settings.MultipleInstances = _TASK_INSTANCES_POLICY.TASK_INSTANCES_IGNORE_NEW;

                folder.RegisterTaskDefinition(
                    TaskName,
                    task,
                    (int)_TASK_CREATION.TASK_CREATE_OR_UPDATE,
                    null,
                    null,
                    _TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN,
                    "");

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"添加开机自启失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除任务
        /// </summary>
        private static void DeleteTask(string taskName)
        {
            TaskSchedulerClass ts = new TaskSchedulerClass();
            ts.Connect(null, null, null, null);
            ITaskFolder folder = ts.GetFolder("\\");
            folder.DeleteTask(taskName, 0);
        }

        /// <summary>
        /// 移除开机自启
        /// </summary>
        public static bool RemoveStart()
        {
            try
            {
                DeleteTask(TaskName);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"移除开机自启失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否已存在开机自启任务
        /// </summary>
        public static bool IsExists(string taskName = TaskName)
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
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"检查开机自启状态失败: {ex.Message}");
            }
            return isExists;
        }

        /// <summary>
        /// 获取所有任务
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

