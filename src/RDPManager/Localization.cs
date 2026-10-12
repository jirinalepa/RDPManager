using System.Globalization;
using RDPManager.Models;
using RDPManager.Properties;

namespace RDPManager;

/// <summary>
/// Selects the UI language. XAML reads texts via {x:Static}, so a change takes effect for windows
/// created afterwards; the long-lived panel and tray menu need an application restart.
/// </summary>
public static class Localization
{
    // Captured before the app overrides the UI culture.
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    public static CultureInfo Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Czech => CultureInfo.GetCultureInfo("cs-CZ"),
        AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
        _ => SystemCulture.TwoLetterISOLanguageName == "cs" ? CultureInfo.GetCultureInfo("cs-CZ") : CultureInfo.GetCultureInfo("en-US"),
    };

    public static void Apply(AppLanguage language)
    {
        var culture = Resolve(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.Culture = culture;
    }

    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
