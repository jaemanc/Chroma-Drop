// MainMenu.cs — 메인 화면(MainGame 씬)의 버튼을 게임 씬에 잇는다.
// 버튼은 씬에 직접 배치돼 있으므로 이름으로 찾아 연결한다.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public const string GameScene = "Main";

    void Awake()
    {
        foreach (var b in GetComponentsInChildren<Button>(true))
        {
            switch (b.name)
            {
                case "Btn_Play":
                case "Btn_Classic": b.onClick.AddListener(() => Launch(false)); break;
                case "Btn_Rush": b.onClick.AddListener(() => Launch(true)); break;
            }
        }
    }

    static void Launch(bool timeAttack)
    {
        GameManager.LaunchTimeAttack = timeAttack;
        SceneManager.LoadScene(GameScene);
    }
}
