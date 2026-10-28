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
            _pending.TryGetValue(operation, out GearSwapEdit edit);
            _pending.Remove(operation);
            IResult result = new FailedResult(pitFireTeam.GetSocialUiText("SwapGearActionBlocked"), 0);
            operation.Status = EOperationStatus.Started;
            try
            {
                if (Session != null && !Session.Applying && edit != null)
                {
                    Session.Stage(edit, this);
                    result = SuccessfulResult.New;
                }
            }
            catch (Exception)
            {
                // Rejected edits never enter native async execution.
            }
            operation.Status = result.Succeed ? EOperationStatus.Succeeded : EOperationStatus.Failed;
            operation.Dispose();
            // A UI callback exception must not be caught as an edit failure and invoked again.
            callback?.Invoke(result);
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
