using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using AbstractOperation = EFT.InventoryLogic.Operations.AbstractOperation;

namespace pitTeam.Modules
{
    /// <summary>Local-only controller: no backend, player hands, health, or live item operations.</summary>
    internal sealed class GearSwapInventoryController : InventoryController
    {
        private readonly Dictionary<AbstractOperation, GearSwapEdit> _pending = new Dictionary<AbstractOperation, GearSwapEdit>();
        internal TeammateGearSwap Session;

        internal GearSwapInventoryController(Profile profile) : base(profile, true) { }

        public override bool IsAllowedToSeeSlot(Slot slot, EquipmentSlot slotName) =>
            Session != null && ReferenceEquals(slot.ParentItem, Session.FollowerEquipment)
                ? Session.CanSeeFollowerSlot(slotName) && !slot.Deleted
                : base.IsAllowedToSeeSlot(slot, slotName);

        public override bool IsAllowedToSeeEquipmentSlot(Slot slot, EquipmentSlot slotName) =>
            Session != null && ReferenceEquals(slot.ParentItem, Session.FollowerEquipment)
                ? Session.CanSeeFollowerSlot(slotName) && !slot.Deleted
                : base.IsAllowedToSeeEquipmentSlot(slot, slotName);

        public override AbstractOperation ConvertOperationResultToOperation(IOperationResult result)
        {
            // Native async-operation constructors publish Begin immediately. The draft owns
            // its own Begin/Succeed pair, so constructing a native move leaks a busy event.
            AbstractOperation operation = new DraftOperation(GetAndIncrementNextOperationId(), this);
            try { _pending[operation] = GearSwapEdit.Capture(result); }
            catch (InvalidOperationException) { /* Execute rejects unsupported actions, including discard. */ }
            return operation;
        }

        public override void Execute(AbstractOperation operation, Callback callback)
        {
            if (Session == null || Session.Applying || !_pending.TryGetValue(operation, out GearSwapEdit edit))
            {
                callback?.Invoke(new FailedResult(pitFireTeam.GetSocialUiText("SwapGearActionBlocked"), 0));
                return;
            }
            _pending.Remove(operation);
            try
            {
                Session.Stage(edit, this);
                callback?.Invoke(SuccessfulResult.New);
            }
            catch (Exception)
            {
                callback?.Invoke(new FailedResult(pitFireTeam.GetSocialUiText("SwapGearActionBlocked"), 0));
            }
        }

        private sealed class DraftOperation : AbstractOperation
        {
            private readonly GearSwapInventoryController _owner;
            internal DraftOperation(ushort id, GearSwapInventoryController owner) : base(id, owner)
            {
                _owner = owner;
            }

            public override void ExecuteInternal(Callback callback) => _owner.Execute(this, callback);
            public override InventoryOperationDescriptor ToDescriptor() =>
                throw new InvalidOperationException("Draft operations cannot be serialized.");
            public override EFT.InventoryLogic.Operations.BaseInventoryCommand ToBaseInventoryCommand(string ownerId) =>
                throw new InvalidOperationException("Draft operations cannot be sent to the backend.");
            public override void Dispose() => _owner._pending.Remove(this);
        }
    }
}
