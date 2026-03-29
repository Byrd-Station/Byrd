// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Paper;
using Content.Server.Complaints.Components;
using Robust.Shared.Localization;
using Robust.Shared.GameObjects;
using JetBrains.Annotations;

namespace Content.Server.Complaints.Systems
{
    [UsedImplicitly]
    public sealed class ComplaintsPaperSystem : EntitySystem
    {
        [Dependency] private readonly PaperSystem _paper = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ComplaintsPaperComponent, MapInitEvent>(OnMapInit);
        }

        private void OnMapInit(EntityUid uid, ComplaintsPaperComponent comp, MapInitEvent args)
        {
            if (TryComp<PaperComponent>(uid, out var paper))
            {
                _paper.SetContent((uid, paper), Loc.GetString("complaints-paper-content"));
            }
        }
    }
}
