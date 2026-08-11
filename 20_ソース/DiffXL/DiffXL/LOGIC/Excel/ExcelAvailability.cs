using System;
using System.Runtime.InteropServices;
using DiffXL.COMMON;

namespace DiffXL.LOGIC.Excel
{
    /// <summary>
    /// デスクトップ Excel が COM で利用可能か調べる。
    /// </summary>
    public static class ExcelAvailability
    {
        /// <summary>
        /// Excel.Application の ProgID。
        /// </summary>
        public const string ExcelProgId = "Excel.Application";

        /// <summary>
        /// Excel.Application が登録されているか。
        /// </summary>
        /// <returns>登録されていれば true</returns>
        public static bool IsExcelInstalled()
        {
            return Type.GetTypeFromProgID(ExcelProgId) != null;
        }

        /// <summary>
        /// Excel の ProgID を取得する。
        /// </summary>
        /// <param name="progId">取得した ProgID</param>
        /// <returns>取得できれば true</returns>
        public static bool TryGetExcelProgId(out string progId)
        {
            if (IsExcelInstalled())
            {
                progId = ExcelProgId;
                return true;
            }

            progId = null;
            return false;
        }

        /// <summary>
        /// ユーザー向けの診断メッセージを返す。
        /// </summary>
        /// <returns>診断メッセージ</returns>
        public static string GetDiagnosticMessage()
        {
            if (!IsExcelInstalled())
            {
                return "Microsoft Excel（デスクトップ版）が見つかりません。DiffXL の表示には Excel が必要です。";
            }

            return "Excel を利用できます。";
        }

        /// <summary>
        /// Excel を起動できるか実際に生成を試みて確認する（ビットネス不一致の検出含む）。
        /// </summary>
        /// <param name="errorMessage">失敗時のユーザー向けメッセージ</param>
        /// <returns>起動可能なら true</returns>
        public static bool CanCreateExcelApplication(out string errorMessage)
        {
            errorMessage = null;
            Type excelType = Type.GetTypeFromProgID(ExcelProgId);
            if (excelType == null)
            {
                errorMessage = GetDiagnosticMessage();
                return false;
            }

            object app = null;
            try
            {
                app = Activator.CreateInstance(excelType);
                if (app == null)
                {
                    errorMessage = "Excel の起動に失敗しました。";
                    return false;
                }

                return true;
            }
            catch (BadImageFormatException ex)
            {
                Log.Exception(ex);
                errorMessage =
                    "Excel のビット数が DiffXL（x64）と一致しません。64 ビット版のデスクトップ Excel をインストールしてください。";
                return false;
            }
            catch (COMException ex)
            {
                Log.Exception(ex);
                // 0x800700C1 = ERROR_BAD_EXE_FORMAT
                if (unchecked((uint)ex.ErrorCode) == 0x800700C1)
                {
                    errorMessage =
                        "Excel のビット数が DiffXL（x64）と一致しません。64 ビット版のデスクトップ Excel をインストールしてください。";
                }
                else
                {
                    errorMessage = "Excel を起動できません: " + ex.Message;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                errorMessage = "Excel を起動できません: " + ex.Message;
                return false;
            }
            finally
            {
                if (app != null)
                {
                    try
                    {
                        dynamic d = app;
                        d.Quit();
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        Marshal.FinalReleaseComObject(app);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
    }
}
