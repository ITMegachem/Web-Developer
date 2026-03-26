using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace Mgt.Lit.WebFront.Services;

public class UiState
{
    private readonly ProtectedSessionStorage _storage;
    private bool _isInitialized = false;

    public string Lang { get; private set; } = "en";
    public event Action? Changed;

    public UiState(ProtectedSessionStorage storage) => _storage = storage;

    public async Task InitAsync()
    {
        if (_isInitialized) return; // ถ้าโหลดแล้วไม่ต้องโหลดซ้ำ ป้องกัน Loop

        try
        {
            var saved = await _storage.GetAsync<string>("lang");
            if (saved.Success && !string.IsNullOrWhiteSpace(saved.Value))
            {
                Lang = saved.Value;
                _isInitialized = true;
                Changed?.Invoke();
            }
        }
        catch (InvalidOperationException)
        {
            // เจอบ่อยใน Blazor Server: พยายามเรียก JS ก่อนเวลาอันควร
            // ไม่ต้องทำอะไร ปล่อยให้มันไปรันใน OnAfterRenderAsync
        }
    }

    public async Task SetLangAsync(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || lang == Lang) return;

        Lang = lang;
        try
        {
            await _storage.SetAsync("lang", Lang);
            Changed?.Invoke();
        }
        catch { /* ป้องกัน Refresh เมื่อ Storage หลุด */ }
    }
}