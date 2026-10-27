// 외부 서비스 연결 전 앱 전체를 오프라인으로 확인하는 테스트 모드다.
public static class TestServices
{
    public static bool Enabled { get { return true; } }
    public static bool DevelopmentToolsEnabled
    {
        get { return UnityEngine.Application.isEditor || UnityEngine.Debug.isDebugBuild; }
    }
}
