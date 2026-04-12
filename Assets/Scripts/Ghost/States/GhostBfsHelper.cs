using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GhostStateDead / GhostStateExitHouse が共用する BFS（幅優先探索）ユーティリティ。
/// </summary>
/// <remarks>
/// 壁判定のみを行い、赤ゾーン・U ターン制限は適用しない。
/// グリーディ法はゴーストハウス周辺で局所解に陥るため採用しない。
/// </remarks>
internal static class GhostBfsHelper
{
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int( 0, -1), // 上
        new Vector2Int(-1,  0), // 左
        new Vector2Int( 0,  1), // 下
        new Vector2Int( 1,  0), // 右
    };

    /// <summary>
    /// BFS で start から goal への最短経路を探索し、最初の 1 ステップ方向を返します。
    /// start == goal の場合または経路が存在しない場合は Vector2Int.zero を返します。
    /// </summary>
    internal static Vector2Int FirstStep(BaseGhost host, Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return Vector2Int.zero;

        // parent[tile] = そのタイルへ来た一手前のタイル（start は自己参照で番兵）
        var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
        var queue  = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            foreach (Vector2Int d in Dirs)
            {
                Vector2Int next = current + d;
                if (parent.ContainsKey(next) || !host.InternalIsPassableForDeadGhost(next))
                    continue;

                parent[next] = current;

                if (next == goal)
                {
                    // goal から start まで親を辿り、start の直接の子を探す
                    Vector2Int step = goal;
                    while (parent[step] != start)
                        step = parent[step];
                    return step - start; // start → step の方向ベクトル
                }

                queue.Enqueue(next);
            }
        }

        return Vector2Int.zero; // 経路なし
    }
}
