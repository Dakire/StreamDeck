using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Signature Authenticode d'un fichier, vérifiée par <c>WinVerifyTrust</c> (le contrôle de Windows lui-même : empreinte du fichier, chaîne de certificats jusqu'à une
/// racine approuvée, validité). La révocation n'est pas interrogée en ligne (une mise à jour ne doit pas dépendre d'un serveur de listes de révocation).
/// </summary>
internal sealed class AuthenticodeVerifier : ISignatureVerifier
{
    // WINTRUST_ACTION_GENERIC_VERIFY_V2
    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    const uint WtdUiNone = 2, WtdRevokeNone = 0, WtdChoiceFile = 1, WtdStateVerify = 1, WtdStateClose = 2;
    const uint WtdRevocationCheckNone = 0x10;
    // Absence de signature : le sujet n'est pas signé, ou son format n'est pas géré
    const uint TrustENoSignature = 0x800B0100, TrustEProviderUnknown = 0x800B0001, TrustESubjectFormUnknown = 0x800B0003;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WintrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, ref WintrustData data);

    /// <inheritdoc/>
    public SignatureInfo Verify(string path)
    {
        if (!File.Exists(path)) return new SignatureInfo(SignatureState.Invalid);
        var file = new WintrustFileInfo { pcwszFilePath = path };
        file.cbStruct = (uint)Marshal.SizeOf(file);
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(), dwUIChoice = WtdUiNone, fdwRevocationChecks = WtdRevokeNone, dwUnionChoice = WtdChoiceFile,
                pFile = pFile, dwStateAction = WtdStateVerify, dwProvFlags = WtdRevocationCheckNone,
            };
            uint result = unchecked((uint)WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data));
            data.dwStateAction = WtdStateClose;
            _ = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);   // libère l'état de vérification

            if (result == 0) return new SignatureInfo(SignatureState.Valid, Publisher(path));
            if (result is TrustENoSignature or TrustEProviderUnknown or TrustESubjectFormUnknown) return new SignatureInfo(SignatureState.NotSigned);
            Log.Warn($"Signature de {Path.GetFileName(path)} refusée (WinVerifyTrust 0x{result:X8})");
            return new SignatureInfo(SignatureState.Invalid);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException or InvalidOperationException)
        {
            Log.Warn($"Vérification de signature impossible : {ex.Message}");
            return new SignatureInfo(SignatureState.Invalid);
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    /// <summary>Sujet du certificat de signature (« CN=Mon Éditeur, O=… »), ou null s'il est illisible.</summary>
    static string? Publisher(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile : seul moyen de lire le certificat de signature d'un exécutable ; la confiance vient de WinVerifyTrust
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException) { return null; }
    }
}
