﻿using System;

namespace RX_Explorer.Class
{
    public abstract class OperationListUndoModel : OperationListBaseModel
    {
        public override string OperationKindText
        {
            get
            {
                return Globalization.GetString("TaskList_OperationKind_Undo");
            }
        }

        protected OperationListUndoModel(EventHandler OnCompleted = null, EventHandler OnErrorHappended = null, EventHandler OnCancelled = null) : base(OnCompleted, OnErrorHappended, OnCancelled)
        {

        }
    }
}
