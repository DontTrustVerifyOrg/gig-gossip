using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GigGossipSettler.Exceptions
{
    public class NotFoundException: SettlerException
    {
        public NotFoundException() : base(SettlerErrorCode.NotFound)
        {
        }
    }
}
