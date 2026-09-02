// ChromaVersion.cs — 앱 버전의 유일한 출처.
// 빌드 스크립트가 이 값을 PlayerSettings.bundleVersion 에 넣고,
// 런타임은 Application.version 으로 읽는다. 손으로 ProjectSettings 를 고치지 않는다.

public static class ChromaVersion
{
    public const string Value = "2.0";
}
