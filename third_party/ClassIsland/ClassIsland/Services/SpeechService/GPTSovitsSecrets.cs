namespace ClassIsland.Services.SpeechService;

public static partial class GptSovitsSecrets
{
#if !PublishBuilding || EXUSIAI_EMBEDDED_SOURCE
    public const string PrivateKey = "";

    public const string PrivateKeyPassPhrase = "";

    public const bool IsSecretsFilled = false;
#endif
}
