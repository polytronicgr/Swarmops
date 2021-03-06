using Swarmops.Common.Interfaces;

namespace Swarmops.Basic.Types.System
{
    public class BasicAddressValidationRun : IHasIdentity
    {
        private readonly int validationRunId;

        public BasicAddressValidationRun (int validationRunId)
        {
            this.validationRunId = validationRunId;
        }

        public int Identity
        {
            get { return this.validationRunId; }
        }
    }
}