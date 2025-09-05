using System.ComponentModel;
using System.Globalization;
using System.Resources;
using FileStrider.Core.Contracts;
using FileStrider.Core.Models;

namespace FileStrider.Infrastructure.Localization;

/// <summary>
/// Service for managing application localization and language switching.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private string _currentLanguage = "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                // Notify that all localized strings should be updated
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            }
        }
    }

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; } = new List<LanguageInfo>
    {
        new("en", "English", "English", "🇺🇸"),
        new("es", "Spanish", "Español", "🇪🇸"),
        new("fr", "French", "Français", "🇫🇷")
    };

    public LocalizationService()
    {
        _resourceManager = new ResourceManager("FileStrider.Infrastructure.Resources.Strings", typeof(LocalizationService).Assembly);
    }

    public void ChangeLanguage(string languageCode)
    {
        if (AvailableLanguages.Any(l => l.Code == languageCode))
        {
            CurrentLanguage = languageCode;
            CultureInfo.CurrentUICulture = new CultureInfo(languageCode);
        }
    }

    public string GetString(string key)
    {
        try
        {
            var culture = new CultureInfo(_currentLanguage);
            var value = _resourceManager.GetString(key, culture);
            return value ?? $"[Missing: {key}]";
        }
        catch
        {
            return $"[Error: {key}]";
        }
    }
}