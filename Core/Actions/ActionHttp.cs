using System;
using System.Net.Http;
using System.Threading;

namespace StreamDak.Actions;

/// <summary>Client HTTP des actions « web » (webhooks).</summary>
public static class ActionHttp
{
    /// <summary>
    /// Client partagé des actions : les redirections ne sont JAMAIS suivies. Un webhook saisi par l'utilisateur ne doit pas être renvoyé
    /// (avec son corps et ses en-têtes d'autorisation) vers une autre adresse choisie par le serveur. Le délai de chaque étape est géré par le moteur.
    /// </summary>
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StreamDak/1.0");
        return http;
    }
}
