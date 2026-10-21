// MainMenu.cs — 메인 화면(Menu 씬)의 버튼을 게임 씬에 잇는다.
// 버튼은 씬에 직접 배치돼 있으므로 이름으로 찾아 연결한다.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public const string GameScene = "Game";

    void Awake()
    {
        Transform playParent = null;
        foreach (var b in GetComponentsInChildren<Button>(true))
        {
            switch (b.name)
            {
                case "Btn_Play": playParent = b.transform.parent; goto case "Btn_Classic";
                case "Btn_Classic": b.onClick.AddListener(() => Launch(false)); break;
                case "Btn_Rush": b.onClick.AddListener(() => Launch(true)); break;
            }
        }
        if (playParent != null) AddFeverTestButton(playParent);
    }

    static void Launch(bool timeAttack)
    {
        GameManager.LaunchFeverTest = false;
        GameManager.LaunchTimeAttack = timeAttack;
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>피버 테스트 버튼. 누르면 판이 시작하자마자 피버가 켜지고, 꺼지면 바로 다시 켜진다 —
    /// 연쇄를 만들지 않고도 피버 연출만 계속 볼 수 있다. 씬이 아니라 코드로 만든다 (테스트용).</summary>
    void AddFeverTestButton(Transform parent)
    {
        var go = new GameObject("Btn_FeverTest", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 180);
        rt.sizeDelta = new Vector2(300, 100);

        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 0.35f, 0.18f);
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            GameManager.LaunchFeverTest = true;
            GameManager.LaunchTimeAttack = false;
            SceneManager.LoadScene(GameScene);
        });

        var lrt = new GameObject("label", typeof(RectTransform)).GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var t = lrt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = "FEVER TEST";
        t.fontSize = 42;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
    }
}
