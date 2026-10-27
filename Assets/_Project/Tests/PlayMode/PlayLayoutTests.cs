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
}
