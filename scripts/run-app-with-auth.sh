#!/bin/bash

# Run the .NET example app with production-like auth settings
# Use this in a separate terminal after starting test-production-auth.sh

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Check if signing key is set
if [ -z "$INNGEST_SIGNING_KEY" ]; then
    echo -e "${RED}Error: INNGEST_SIGNING_KEY not set${NC}"
    echo -e "Run ${YELLOW}source <env_file>${NC} first (path shown by test-production-auth.sh)"
    echo -e ""
    echo -e "Or set manually:"
    echo -e "  export INNGEST_SIGNING_KEY=\"signkey-test-<your-key>\""
    echo -e "  export INNGEST_EVENT_KEY=\"your-event-key\""
    echo -e "  export INNGEST_DEV=\"false\""
    exit 1
fi

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}Running .NET App with Production Auth${NC}"
echo -e "${BLUE}========================================${NC}"
echo -e ""
echo -e "${GREEN}Settings:${NC}"
echo -e "  INNGEST_SIGNING_KEY: ${YELLOW}${INNGEST_SIGNING_KEY:0:30}...${NC}"
echo -e "  INNGEST_EVENT_KEY:   ${YELLOW}${INNGEST_EVENT_KEY}${NC}"
echo -e "  INNGEST_DEV:         ${YELLOW}${INNGEST_DEV}${NC}"
echo -e ""

cd "$(dirname "$0")/.."

echo -e "${GREEN}Building...${NC}"
dotnet build InngestExample.sln --verbosity quiet

echo -e "${GREEN}Starting app on http://localhost:5000${NC}"
echo -e "${YELLOW}Open http://localhost:8288 to see the Inngest dashboard${NC}"
echo -e ""

dotnet run --project InngestExample --no-build
