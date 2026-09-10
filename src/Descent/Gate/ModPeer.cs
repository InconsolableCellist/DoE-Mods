namespace Descent.Gate
{
    /// <summary>One occupant of the current room, as the roster sees them.</summary>
    public class ModPeer
    {
        public int ActorNumber;
        public string NickName;
        public bool IsLocal;

        /// <summary>Null when this player advertises no <c>dd.*</c> properties — i.e. not running Descent.</summary>
        public string Version;
        public string Sha;
        public ModCaps Caps;

        /// <summary>The game's own player property, useful context for skew diagnosis.</summary>
        public string GameBuild;

        public bool IsModded => Version != null && Sha != null;

        public override string ToString() =>
            IsModded
                ? $"actor {ActorNumber} `{NickName}` — dd {Version} / {Sha} / caps {ModCapsInfo.Describe(Caps)}"
                : $"actor {ActorNumber} `{NickName}` — no Descent (no dd.* properties)";
    }
}
