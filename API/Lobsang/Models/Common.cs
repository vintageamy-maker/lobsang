namespace Lobsang.Models
{
    /// <summary>稽核欄位：由 AuditableInterceptor 於 SaveChanges 統一寫入，實體程式碼不各自維護。</summary>
    public interface IAuditable
    {
        DateTime CreatedAt { get; set; }
        DateTime UpdatedAt { get; set; }
        int? CreatedBy { get; set; }
        int? UpdatedBy { get; set; }
    }

    /// <summary>軟刪除：個案等依法需保留軌跡的資料不做硬刪，查詢由 global query filter 過濾。</summary>
    public interface ISoftDelete
    {
        DateTime? DeletedAt { get; set; }
    }
}
