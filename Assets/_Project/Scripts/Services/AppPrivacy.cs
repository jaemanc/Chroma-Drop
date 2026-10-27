using System;
using UnityEngine;

public static class AppPrivacy
{
    [Serializable]
    class Info { public string privacyPolicyUrl = ""; public string supportEmail = ""; }
    static Info info;
    public static string PolicyUrl
    {
        get
        {
            if (info == null)
            {
                var file = Resources.Load<TextAsset>("app-info");
                try { info = file == null ? new Info() : JsonUtility.FromJson<Info>(file.text); }
                catch (ArgumentException) { info = new Info(); }
            }
            Uri url;
            return info != null && Uri.TryCreate(info.privacyPolicyUrl, UriKind.Absolute, out url) && url.Scheme == "https"
                ? url.AbsoluteUri : "";
        }
    }
    public static bool HasPolicyUrl { get { return PolicyUrl.Length > 0; } }
    public static void OpenPolicy() { if (HasPolicyUrl) Application.OpenURL(PolicyUrl); }
}
