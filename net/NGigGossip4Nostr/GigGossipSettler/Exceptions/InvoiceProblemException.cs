using System;
using GigLNDWalletAPIClient;

namespace GigGossipSettler.Exceptions
{
	public class InvoiceProblemException : SettlerException
    {
        LNDWalletErrorCode LNDWalletErrorCode;
        public InvoiceProblemException(LNDWalletErrorCode LNDWalletErrorCode) : base(SettlerErrorCode.InvoiceProblem)
        {
            this.LNDWalletErrorCode = LNDWalletErrorCode;
        }
	}
}

