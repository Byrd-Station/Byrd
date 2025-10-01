using Robust.Shared.Prototypes;
using Content.Shared._Starlight.CollectiveMind;

namespace Content.Shared._Omu.Clothing.Components
{
    [RegisterComponent]
    public sealed partial class ClothingGrantCollectiveMindComponent : Component
    {
        [DataField("minds", required: true)]
        public List<ProtoId<CollectiveMindPrototype>> Minds = new();
        [DataField]
        public ProtoId<CollectiveMindPrototype>? defaultChannel = null;
    }
}
