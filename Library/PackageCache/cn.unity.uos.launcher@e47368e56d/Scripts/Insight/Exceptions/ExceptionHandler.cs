using System;
using System.Collections.Generic;
using Unity.UOS.Insight.Models;
using UnityEngine;

namespace Unity.UOS.Insight.Exceptions
{
    public class ExceptionHandler
    {

        //Whether to exit the program when an exception occurs
        public static bool IsQuitWhenException = false;

        //Whether the exception catch has been registered
        public static bool IsRegistered = false;
        private static AutoReportEventHandler mEventCallback;
        private static Dictionary<string, object> mProperties;


        public static void SetAutoReportProperties(Dictionary<string, object> properties)
        {
            if (!(mProperties is Dictionary<string, object>))
            {
                mProperties = new Dictionary<string, object>();
            }

            foreach (var item in properties)
            {
                if (!mProperties.ContainsKey(item.Key))
                {
                    mProperties.Add(item.Key, item.Value);
                }
            }
        }

        public static void RegisterExceptionHandler(AutoReportEventHandler eventCallback)
        {
            mEventCallback = eventCallback;
            //Register exception handling delegate
            try
            {
                if (!IsRegistered)
                {
                    Application.logMessageReceived += _LogHandler;
                    AppDomain.CurrentDomain.UnhandledException += _UncaughtExceptionHandler;
                    IsRegistered = true;
                }
            }
            catch
            {
            }            
        }

        public static void RegisterExceptionHandler(Dictionary<string, object> properties)
        {
            SetAutoReportProperties(properties);
            //Register exception handling delegate
            try
            {
                if (!IsRegistered)
                {
                    Application.logMessageReceived += _LogHandler;
                    AppDomain.CurrentDomain.UnhandledException += _UncaughtExceptionHandler;
                    IsRegistered = true;
                }
            }
            catch
            {
            }
        }

        public static void UnregisterExceptionHandler ()
        {
            //Clear exception handling delegate
            try
            {
                Application.logMessageReceived -= _LogHandler;
                System.AppDomain.CurrentDomain.UnhandledException -= _UncaughtExceptionHandler;
            }
            catch
            {
            }
        }
    
    
        private static void _LogHandler( string logString, string stackTrace, LogType type )
        {
            if( type == LogType.Error || type == LogType.Exception || type == LogType.Assert )
            {
                //Report exception event
                string reasonStr = "exception_type: " + type.ToString() + " <br> " + "exception_message: " + logString + " <br> " + "stack_trace: " + stackTrace + " <br> " ; 
                Dictionary<string, object> properties = new Dictionary<string, object>(){
                    {"#app_crashed_reason", reasonStr}
                };
                properties = MergeProperties(properties);
                InsightSDK.ReportEvent("app_crash", properties);

                if ( IsQuitWhenException )
                {
                    Application.Quit();
                }
            }
        }

        private static void _UncaughtExceptionHandler (object sender, System.UnhandledExceptionEventArgs args)
        {
            if (args == null || args.ExceptionObject == null)
            {
                return;
            }
            
            try
            {
                if (args.ExceptionObject.GetType () != typeof(System.Exception))
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            System.Exception e = (System.Exception)args.ExceptionObject;

            //Report exception event
            string reasonStr = "exception_type: " + e.GetType().Name + " <br> " + "exception_message: " + e.Message + " <br> " + "stack_trace: " + e.StackTrace + " <br> " ; 
            Dictionary<string, object> properties = new Dictionary<string, object>(){
                {"#app_crashed_reason", reasonStr}
            };
            properties = MergeProperties(properties);
            InsightSDK.ReportEvent("app_crash", properties);

            if ( IsQuitWhenException )
            {
                Application.Quit();
            }
        }

        private static Dictionary<string, object> MergeProperties(Dictionary<string, object> properties)
        {

            if (mEventCallback is AutoReportEventHandler)
            {
                Dictionary<string, object> callbackProperties = mEventCallback.GetAutoReportEventProperties((int)AutoReportEventType.AppCrash, properties);
                foreach (var item in callbackProperties)
                {
                    if (!properties.ContainsKey(item.Key))
                    {
                        properties.Add(item.Key, item.Value);
                    }
                }
            }

            if (mProperties is Dictionary<string, object>)
            {
                foreach (var item in mProperties)
                {
                    if (!properties.ContainsKey(item.Key))
                    {
                        properties.Add(item.Key, item.Value);
                    }
                }
            }

            return properties;
        }
    }
}