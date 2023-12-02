using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GigGossipSettler.Exceptions
{
    public class UnknownCertificateException: SettlerException
    {
        public UnknownCertificateException() : base(SettlerErrorCode.UnknownCertificate)
        {
        }
    }
}
