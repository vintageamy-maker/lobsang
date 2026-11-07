namespace Lobsang.Services
{
    /// <summary>
    /// 使用者送進來的內容不合法（不是系統故障）。
    /// 在任何深度丟出都會被 <see cref="Middleware.ExceptionHandlingMiddleware"/> 轉成 400 ＋ 原因，
    /// 而且**不寫錯誤日誌** —— 混進錯誤日誌會讓真正的故障被淹沒。
    /// </summary>
    public class UserInputException(string message, string code) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
