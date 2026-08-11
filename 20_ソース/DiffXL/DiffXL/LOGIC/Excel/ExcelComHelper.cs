using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.CompilerServices;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// Excel COM の遅延バインディング。
    /// __ComObject では GetType().InvokeMember が失敗しやすいため LateGet/LateSet を優先する。
    /// </summary>
    internal static class ExcelComHelper
    {
        /// <summary>
        /// プロパティ取得。
        /// </summary>
        public static bool TryGetProperty(object target, string name, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            try
            {
                // COM は Visual Basic の LateGet が最も安定
                value = NewLateBinding.LateGet(
                    target,
                    null,
                    name,
                    new object[0],
                    null,
                    null,
                    null);
                return true;
            }
            catch
            {
                try
                {
                    value = target.GetType().InvokeMember(
                        name,
                        BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                        null,
                        target,
                        null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// プロパティ設定。
        /// </summary>
        public static bool TrySetProperty(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            try
            {
                NewLateBinding.LateSet(
                    target,
                    null,
                    name,
                    new object[] { value },
                    null,
                    null);
                return true;
            }
            catch
            {
                try
                {
                    target.GetType().InvokeMember(
                        name,
                        BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                        null,
                        target,
                        new[] { value });
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// メソッド呼び出し。
        /// </summary>
        public static bool TryInvoke(object target, string name, object[] args, out object result)
        {
            result = null;
            if (target == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            try
            {
                result = NewLateBinding.LateCall(
                    target,
                    null,
                    name,
                    args ?? new object[0],
                    null,
                    null,
                    null,
                    true);
                return true;
            }
            catch
            {
                try
                {
                    result = target.GetType().InvokeMember(
                        name,
                        BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                        null,
                        target,
                        args ?? new object[0]);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 一時 COM を 1 回だけ解放する（FinalRelease は使わない）。
        /// ActiveWindow 等は解放しないこと。
        /// </summary>
        public static void SafeRelease(object com)
        {
            if (com == null || !Marshal.IsComObject(com))
            {
                return;
            }

            try
            {
                Marshal.ReleaseComObject(com);
            }
            catch
            {
                // ignore
            }
        }
    }
}
