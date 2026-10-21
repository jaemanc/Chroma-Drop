using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 배너 아트 위의 문구는 실제 폰트로 표시해 목표와 언어가 바뀌어도 같은 스타일을 유지한다.
public class MissionBanner : MonoBehaviour
{
    readonly TMP_Text[] rows = new TMP_Text[3];
    TMP_Text progress;

    static RectTransform Box(string name, Transform parent, float x, float top, float w, float h)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x, 1f - top - h);
        rt.anchorMax = new Vector2(x + w, 1f - top);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TMP_Text Label(string name, Transform parent, UiFontRole role)
    {
        var text = Box(name, parent, 0, 0, 1, 1).gameObject.AddComponent<TextMeshProUGUI>();
        UiTypography.Apply(text, role);
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10;
        text.fontSizeMax = 48;
        text.color = Color.white;
        text.outlineColor = new Color32(17, 27, 103, 255);
        text.outlineWidth = 0.12f;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    public void Build(Texture2D banner)
    {
        var picture = Box("decoration", transform, 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
        picture.texture = banner;
        picture.raycastTarget = false;
        var panel = Box("missionpanel", transform, 0.285f, 0.47f, 0.425f, 0.265f);
        for (int i = 0; i < rows.Length; i++) rows[i] = Label("missionrow" + i, panel, UiFontRole.Body);
        var chip = Box("liveprogress", transform, 0.727f, 0.55f, 0.15f, 0.17f);
        progress = Label("progress", chip, UiFontRole.Numbers);
        progress.color = new Color32(26, 39, 121, 255);
        progress.outlineWidth = 0;
        progress.fontSizeMax = 44;
    }

    void SetRow(int index, int count, string message)
    {
        var row = rows[index];
        row.gameObject.SetActive(true);
        var rt = row.rectTransform;
        float h = 0.90f / count;
        rt.anchorMin = new Vector2(0.025f, 1f - 0.05f - (index + 1) * h);
        rt.anchorMax = new Vector2(0.975f, 1f - 0.05f - index * h);
        row.text = message;
    }

    public void Refresh(GameManager game)
    {
        for (int i = 0; i < rows.Length; i++) rows[i].gameObject.SetActive(false);
        if (game.FeverOn)
        {
            SetRow(0, 1, "Fever time " + game.FeverLeftSec.ToString("0.0"));
            progress.text = game.FeverLeftSec.ToString("0.0") + "s";
        }
        else if (game.TimeAttackMode)
        {
            SetRow(0, 1, "Time attack");
            int seconds = Mathf.CeilToInt(game.TimeLeftSec);
            progress.text = seconds / 60 + ":" + (seconds % 60).ToString("00");
        }
        else
        {
            int count = (game.MarksTotal > 0 ? 1 : 0) + (game.PieceLimited ? 1 : 0) + (game.ClearTarget > 0 ? 1 : 0);
            int row = 0;
            if (game.MarksTotal > 0) SetRow(row++, count, "Pop target blocks");
            if (game.PieceLimited) SetRow(row++, count, "Only " + game.Stage.PieceLimit + " pieces");
            if (game.ClearTarget > 0) SetRow(row++, count, "Clear " + game.ClearTarget + " blocks");
            if (count == 0) SetRow(0, 1, "Keep popping!");
            progress.text = game.MarksTotal > 0 ? (game.MarksTotal - game.MarksLeft) + "/" + game.MarksTotal
                : game.ClearTarget > 0 ? Mathf.Min(game.Broken, game.ClearTarget) + "/" + game.ClearTarget : "";
        }
    }
}
