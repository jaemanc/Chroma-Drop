using NUnit.Framework;
using UnityEngine;

public class PlayLayoutTests
{
    [TestCase(390, 780)]
    [TestCase(375, 600)]
    [TestCase(768, 960)]
    [TestCase(1024, 1300)]
    [TestCase(690, 800)]
    public void BoardUsesSafeWidthAndLeavesRoomForControls(float width, float height)
    {
        var board = GameUI.NightBoardRect(new Vector2(width, height));
        float scale = Mathf.Min(width / 1024f, height / 1150f);
        Assert.That(board.width, Is.EqualTo(board.height));
        Assert.That(board.width * scale, Is.GreaterThan(width * .70f));
        Assert.That(board.xMin, Is.GreaterThanOrEqualTo(0));
        Assert.That(board.xMax, Is.LessThanOrEqualTo(1024));
        Assert.That((board.yMax + 236) * scale, Is.LessThanOrEqualTo(height));
    }
    [Test]
    public void HeldPieceWarnsOnlyInFinalSecondAndResetsWhenRedrawn()
    {
        var root = new GameObject("제한시간 경고 검증");
        try
        {
            var view = root.AddComponent<BoardView>();
            view.Build();
            var piece = new ColorMatcher.Core.Piece("test", new System.Collections.Generic.List<ColorMatcher.Core.Point> {
                new ColorMatcher.Core.Point(0, 0)
            }, 0);
            var field = typeof(BoardView).GetField("ghost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var ghost = ((SpriteRenderer[])field.GetValue(view))[0];
            view.ShowGhost(piece, 0, 0, 0, 0, true, Color.blue);
            var normal = ghost.color;
            view.ApplyHeldPieceWarning(1.1f);
            Assert.That(ghost.color, Is.EqualTo(normal));
            view.ApplyHeldPieceWarning(1f);
            Assert.That(ghost.color.r, Is.GreaterThan(ghost.color.b));
            Assert.That(ghost.color.a, Is.EqualTo(normal.a).Within(.001f));
            view.ShowGhost(piece, 0, 0, 0, 0, true, Color.blue);
            view.ApplyHeldPieceWarning(.5f);
            Assert.That(ghost.color.a, Is.EqualTo(normal.a * .6f).Within(.001f));
            view.ShowGhost(piece, 0, 0, 0, 0, true, Color.blue);
            view.ApplyHeldPieceWarning(0f);
            Assert.That(ghost.color.a, Is.EqualTo(normal.a * .2f).Within(.001f));
            view.HideGhost();
            Assert.That(ghost.enabled, Is.False);
            view.ShowGhost(piece, 0, 0, 0, 0, true, Color.blue);
            Assert.That(ghost.color, Is.EqualTo(normal));
        }
        finally { Object.DestroyImmediate(root); }
    }
}
