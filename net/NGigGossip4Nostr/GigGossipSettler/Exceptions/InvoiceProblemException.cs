using System;
using GigLNDWalletAPIClient;

namespace GigGossipSettler.Exceptions
{
	public class InvoiceProblemException : SettlerException
    {
        GigLNDWalletAPIErrorCode gigLNDWalletAPIErrorCode;
        public InvoiceProblemException(GigLNDWalletAPIErrorCode gigLNDWalletAPIErrorCode) : base(SettlerErrorCode.InvoiceProblem)
        {
            this.gigLNDWalletAPIErrorCode = gigLNDWalletAPIErrorCode;
        }
	}
}

