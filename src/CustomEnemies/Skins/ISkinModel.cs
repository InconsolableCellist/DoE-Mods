using System.Collections.Generic;
using UnityEngine;

namespace CustomEnemies.Skins
{
    /// <summary>What an <see cref="EnemySkin"/> draws in the enemy's place: the built-in placeholder or a bundle skin.</summary>
    public interface ISkinModel
    {
        GameObject Root { get; }
        /// <summary>Every renderer of the model (for visibility and the halo/eye checks).</summary>
        IReadOnlyList<Renderer> Renderers { get; }
        /// <summary>Materials this model owns and may retarget to the enemy's dissolve shader.</summary>
        IReadOnlyList<Material> GameShaderMaterials { get; }
        /// <summary>False while the model waits for the enemy to settle; the skin stays hidden until then.</summary>
        bool Ready { get; }
        /// <summary>Per frame, after the skin root has been put on the enemy root.</summary>
        void Tick(float dt);
        /// <summary>A new spawn from the pool.</summary>
        void Reset();
        void Destroy();
        string Describe();
        /// <summary>Measurements for the report, or null when there is nothing new.</summary>
        string Sample();
        /// <summary>
        /// How much of the enemy the game is drawing, 0..1, from its dissolve. The model's game-shader
        /// materials dissolve with it already; this is for the parts that don't wear that shader (glows).
        /// </summary>
        void SetVisible(float visible);
    }
}
