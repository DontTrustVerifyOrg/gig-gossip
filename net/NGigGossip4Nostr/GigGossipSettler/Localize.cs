using System;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Concurrent;

namespace GigGossipSettler;

public static class Localize
{

    static ConcurrentDictionary<string, IConfigurationRoot> langConf = new();
    static object guard = new();
    static bool initialized = false;

    private static void InitializeStrings()
    {
        lock (guard)
        {
            if (initialized) return;

            var configuredLanguage = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            var loadedLangs = new ConfigurationBuilder().AddIniStream(new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localize", "langs.ini"), FileMode.Open)).Build();
            foreach (var lang in loadedLangs.GetChildren())
                langConf.TryAdd(lang.Key.ToUpper(), new ConfigurationBuilder().AddIniStream(new FileStream(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localize", $"{lang.Key.ToLower()}.ini"), FileMode.Open)).Build());

            initialized = true;
        }
    }

    private static T FillNulls<T>(T x)
    {
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(x))
        {
            if (property.GetValue(x) == null)
            {
                var val = "[" + x.GetType().Name + "." + property.Name + "]";
                Console.WriteLine("NO TRANSLATION WARNING:" + val);
                property.SetValue(x, val);
            }
        }
        return x;
    }

    public static T GetStrings<T, G>(string lang)
    {
        InitializeStrings();
        if (!langConf.ContainsKey(lang))
        {
            Console.WriteLine("NO TRANSLATION FOR LANGUAGE:" + lang);
            lang = "EN";
        }
        return FillNulls(langConf[lang].GetSection(typeof(G).Name).Get<T>());
    }

    public static T GetStrings<T>(string lang)
    {
        InitializeStrings();
        if (!langConf.ContainsKey(lang))
        {
            Console.WriteLine("NO TRANSLATION FOR LANGUAGE:" + lang);
            lang = "EN";
        }
        return FillNulls(langConf[lang].GetSection(typeof(T).Name).Get<T>());
    }

}
