using System;

namespace StreamDak;

/// <summary>État de la signature Authenticode d'un fichier.</summary>
public enum SignatureState
{
    /// <summary>Aucune signature.</summary>
    NotSigned,
    /// <summary>Signature présente, chaîne de confiance et empreinte valides.</summary>
    Valid,
    /// <summary>Signature présente mais refusée (fichier modifié, certificat révoqué, expiré ou non approuvé).</summary>
    Invalid,
}

/// <summary>Signature d'un fichier : son état et le nom de l'éditeur (sujet du certificat) quand elle est valide.</summary>
/// <param name="State">État de la signature.</param>
/// <param name="Publisher">Sujet du certificat de signature, ou null.</param>
public sealed record SignatureInfo(SignatureState State, string? Publisher = null);

/// <summary>Vérifie la signature Authenticode d'un fichier. L'implémentation Windows est dans <c>Platform/Windows</c> ; les tests en fournissent une fausse.</summary>
public interface ISignatureVerifier
{
    /// <summary>Signature du fichier ; ne lève jamais d'exception pour un fichier illisible (rend <see cref="SignatureState.Invalid"/>).</summary>
    SignatureInfo Verify(string path);
}

/// <summary>Règle d'acceptation d'un installateur téléchargé selon sa signature et celle de l'application en cours.</summary>
public static class SignaturePolicy
{
    /// <summary>
    /// Raison du refus, ou null si l'installateur peut être lancé :
    /// <list type="bullet">
    /// <item>signature invalide : toujours refusé ;</item>
    /// <item>signé alors que l'application l'est aussi : le même éditeur est exigé ;</item>
    /// <item>non signé alors que l'application en cours est signée : refusé (une release ne perd pas sa signature) ;</item>
    /// <item>non signé et application non signée : accepté, l'intégrité repose sur <c>checksums.txt</c> ;</item>
    /// <item>signé et application non signée : accepté (la confiance augmente).</item>
    /// </list>
    /// </summary>
    public static string? Refusal(SignatureInfo installer, SignatureInfo current)
    {
        if (installer.State == SignatureState.Invalid) return "La signature de l'installateur n'est pas valide : installation refusée.";
        if (current.State == SignatureState.Valid)
        {
            if (installer.State == SignatureState.NotSigned) return "L'installateur n'est pas signé alors que l'application l'est : installation refusée.";
            if (!string.Equals(installer.Publisher, current.Publisher, StringComparison.OrdinalIgnoreCase))
                return "L'installateur n'est pas signé par le même éditeur que l'application : installation refusée.";
        }
        return null;
    }
}
