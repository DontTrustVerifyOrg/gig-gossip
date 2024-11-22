import os
import json
import requests
from wallet import Wallet
from openai import OpenAI
from termcolor import colored

from rich.console import Console
from rich.markdown import Markdown
from rich.table import Table

exec(open(('./regtest.secret')).read())

os.environ["OPENAI_API_KEY"] = openai_api_key
os.environ["BASE_URL"] = wallet_api

class ChatAgent:
    client = OpenAI()
    GPT_MODEL = "gpt-4o"
    messages = [
        {"role": "system", "content": (
            "You are a Lightning Network wallet management agent. Your task is to assist users in managing their Lightning Network wallet and performing various operations. "
            "The following functionalities are available: "
            "1. Top up and mine blocks (RegTest): Send satoshis to a Bitcoin address and mine 6 blocks for confirmation. "
            "2. Send to address: Transfer Bitcoin from local wallet to a specified address. "
            "3. Generate blocks (RegTest): Mine new blocks for testing purposes. "
            "4. New Bitcoin address: Generate a new Bitcoin address for the local wallet. "
            "5. Get Bitcoin wallet balance: Check the current balance with specified confirmations. "
            "6. Get LND wallet balance: View Lightning Network wallet balance details. "
            "7. Open reserve: Create a new reserve in the LND wallet. "
            "8. Close reserve: Close a specific reserve using its ID. "
            "9. List orphaned reserves: View reserves not associated with payouts. "
            "10. List channels: View active and inactive Lightning Network channels. "
            "11. List closed channels: Review history of closed channels. "
            "12. Estimate fee: Calculate fees for potential payouts. "
            "13. Get balance: View detailed account balance information. "
            "14. New address: Generate address for receiving payments. "
            "15. List transactions: View top-up transaction history. "
            "16. Register payout: Initiate new payout requests. "
            "17. List payouts: View all registered payouts. "
            "18. Get payout: Check specific payout details. "
            "19. Add invoice: Create new payment requests. "
            "20. Add HODL invoice: Create conditional payment requests. "
            "21. Decode invoice: View payment request details. "
            "22. Send payment: Execute Lightning Network payments. "
            "23. Cancel invoice and send payment: Atomic operation combining cancellation and payment. "
            "24. Estimate route fee: Calculate payment routing costs. "
            "25. Settle invoice: Complete HODL invoice payments. "
            "26. Cancel invoice: Cancel unpaid invoices. "
            "27. Get invoice: View specific invoice details. "
            "28. List invoices: View all account invoices. "
            "29. List payments: View successful and in-progress payments. "
            "30. Get payment: Check specific payment details. "
            "You should guide users through these operations, provide relevant information, and handle any errors or issues that may arise. "
            "For any unclear commands or additional information needed, you should ask the user for clarification. Ensure all operations maintain security and accuracy."
            "Output in markdown format. Use tables to display data. Dont use markdown inside tables."
        )}
    ]
    tools = [
        {
            "type": "function",
            "function": {
                "name": "topupandmine6blocks",
                "description": "In RegTest mode only: Sends the specified amount of satoshis from the local Bitcoin wallet to the provided Bitcoin address, then automatically mines 6 blocks to ensure transaction confirmation. This function is useful for testing and development purposes in a controlled environment.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "bitcoinAddr": {
                            "type": "string",
                            "description": "Bitcoin address to receive the funds",
                        },
                        "satoshis": {
                            "type": "integer",
                            "description": "Amount of satoshis to send",
                        }
                    },
                    "required": ["bitcoinAddr", "satoshis"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "sendtoaddress",
                "description": "Transfers the specified amount of satoshis from the local Bitcoin wallet to the provided Bitcoin address. In RegTest mode, this function is available to all users. In other modes (TestNet, MainNet), only administrators can use this function.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "bitcoinAddr": {
                            "type": "string",
                            "description": "Bitcoin address",
                        },
                        "satoshis": {
                            "type": "integer",
                            "description": "Number of satoshis",
                        }
                    },
                    "required": ["bitcoinAddr", "satoshis"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "generateblocks",
                "description": "Mines a specified number of new blocks in the Bitcoin network. This operation is only available in RegTest mode, which is used for testing and development purposes.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "blocknum": {
                            "type": "integer",
                            "description": "The number of new blocks to generate. Must be a positive integer.",
                        }
                    },
                    "required": ["blocknum"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "newbitcoinaddress",
                "description": "Creates and returns a new Bitcoin address associated with the local Bitcoin wallet. This endpoint provides different access levels based on the network mode: in RegTest mode, it's accessible to all users, while in TestNet and MainNet modes, it's restricted to administrators only.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getbitcoinwalletbalance",
                "description": "Fetches and returns the current balance of the Bitcoin wallet in satoshis. The balance returned is based on the specified minimum number of confirmations.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "minConf": {
                            "type": "integer",
                            "description": "The minimum number of confirmations required for transactions to be included in the balance calculation.",
                        }
                    },
                    "required": ["minConf"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getlndwalletbalance",
                "description": "Fetches and returns the current balance of the LND (Lightning Network Daemon) wallet, including confirmed, unconfirmed, total, reserved, and locked balances.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "openreserve",
                "description": "Creates a new reserve in the LND wallet, allocating a specified amount of satoshis.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "satoshis": {
                            "type": "integer",
                            "description": "The amount of satoshis to allocate to the new reserve.",
                        }
                    },
                    "required": ["satoshis"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "closereserve",
                "description": "Closes a previously opened reserve in the LND wallet, identified by its unique GUID.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "reserveId": {
                            "type": "string",
                            "description": "The unique identifier (GUID) of the reserve to be closed.",
                        }
                    },
                    "required": ["reserveId"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listorphanedreserves",
                "description": "Returns an array of unique identifiers (GUIDs) for orphaned reserves in the LND wallet.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listchannels",
                "description": "Retrieves a list of Lightning Network channels associated with the LND node. This endpoint provides detailed information about each channel, including its capacity, balance, and current state. It can be used to monitor the node's connectivity and liquidity within the Lightning Network. The response includes both active and inactive channels, depending on the 'activeOnly' parameter.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "activeOnly": {
                            "type": "boolean",
                            "description": "If true, returns only active channels. If false, returns all channels including inactive ones.",
                        }
                    },
                    "required": ["activeOnly"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listclosedchannels",
                "description": "Retrieves a list of closed Lightning Network channels associated with the LND node. This endpoint provides detailed information about each closed channel, including its capacity, closing transaction, and settlement details. It can be used to review the history of closed channels and analyze past network connections.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "estimatefee",
                "description": "Provides a comprehensive fee estimation for a potential payout that includes both the Bitcoin transaction fee and the Lightning Network channel closing fee. This endpoint calculates the estimated on-chain transaction fee based on the provided destination address and amount, as well as the potential fee for closing a Lightning Network channel.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "address": {
                            "type": "string",
                            "description": "The destination Bitcoin address for the potential payout transaction. This should be a valid Bitcoin address where the funds would be sent.",
                        },
                        "satoshis": {
                            "type": "integer",
                            "description": "The amount of the potential payout, specified in satoshis (1 satoshi = 0.00000001 BTC). This value must be a positive integer representing the exact amount of the intended payout.",
                        }
                    },
                    "required": ["address", "satoshis"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getbalance",
                "description": "This endpoint provides detailed information about the user's account balance. The balance is returned as an AccountBalanceDetails object, which includes the total balance, available balance, and any pending transactions. All amounts are in satoshis (1 BTC = 100,000,000 satoshis).",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "newaddress",
                "description": "Creates and returns a new Bitcoin address associated with the user's Lightning Network account. This address can be used to receive on-chain Bitcoin payments, which will then be credited to the user's Lightning Network balance. This feature enables seamless integration between on-chain and off-chain funds management.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listtransactions",
                "description": "List top-up transactions."
            }
        },
        {
            "type": "function",
            "function": {
                "name": "registerpayout",
                "description": "Initiates a new payout request from the user's Lightning Network wallet to a specified Bitcoin address on the blockchain. This operation registers the payout for execution, which may involve closing Lightning channels if necessary to fulfill the requested amount. The method returns a unique identifier (GUID) for tracking the payout request.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "satoshis": {
                            "type": "integer",
                            "description": "The amount to be paid out, specified in satoshis (1 satoshi = 0.00000001 BTC). Must be a positive integer representing the exact payout amount.",
                        },
                        "btcAddress": {
                            "type": "string",
                            "description": "The destination Bitcoin address where the payout will be sent. This should be a valid Bitcoin address on the blockchain.",
                        }
                    },
                    "required": ["satoshis", "btcAddress"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listpayouts",
                "description": "List registered payouts.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "addinvoice",
                "description": "Creates and returns a new Lightning Network invoice for receiving payments. This endpoint allows users to generate payment requests with customizable amount, memo, and expiration time. The created invoice can be shared with payers to facilitate Lightning Network transactions.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "satoshis": {
                            "type": "integer",
                            "description": "The amount of the invoice in satoshis (1 BTC = 100,000,000 satoshis). Must be a positive integer representing the exact payment amount requested.",
                        },
                        "memo": {
                            "type": "string",
                            "description": "An optional memo or description for the invoice. This can be used to provide additional context or details about the payment to the payer. The memo will be included in the encoded payment request.",
                        },
                        "expiry": {
                            "type": "integer",
                            "description": "The expiration time for the payment request, in seconds. After this duration, the invoice will no longer be valid for payment.",
                        }
                    },
                    "required": ["satoshis", "memo", "expiry"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "addhodlinvoice",
                "description": "Creates and returns a new Lightning Network HODL invoice. HODL invoices enable escrow-like functionalities by allowing the recipient to claim the payment only when a specific preimage is revealed using the SettleInvoice method. This preimage must be provided by the payer or a trusted third party. This mechanism provides an additional layer of security and enables conditional payments in the Lightning Network, making it suitable for implementing escrow accounts and other advanced payment scenarios.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "satoshis": {
                            "type": "integer",
                            "description": "The amount of the invoice in satoshis (1 BTC = 100,000,000 satoshis). Must be a positive integer representing the exact payment amount requested.",
                        },
                        "hash": {
                            "type": "string",
                            "description": "The SHA-256 hash of the preimage. The payer or a trusted third party must provide the corresponding preimage, which will be used with the SettleInvoice method to claim the payment, enabling escrow-like functionality.",
                        },
                        "memo": {
                            "type": "string",
                            "description": "An optional memo or description for the invoice. This can be used to provide additional context or details about the payment or escrow conditions to the payer. The memo will be included in the encoded payment request.",
                        },
                        "expiry": {
                            "type": "integer",
                            "description": "The expiration time for the payment request, in seconds. After this duration, the HODL invoice will no longer be valid for payment. Consider setting an appropriate duration based on the expected escrow period.",
                        }
                    },
                    "required": ["satoshis", "hash", "memo", "expiry"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "decodeinvoice",
                "description": "This endpoint decodes a Lightning Network invoice (also known as a payment request) and returns detailed information about its contents. It provides insights into the payment amount, recipient, expiry time, and other relevant metadata encoded in the invoice.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymentrequest": {
                            "type": "string",
                            "description": "This endpoint decodes a Lightning Network invoice (also known as a payment request) and returns detailed information about its contents. It provides insights into the payment amount, recipient, expiry time, and other relevant metadata encoded in the invoice.",
                        }
                    },
                    "required": ["paymentrequest"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "sendpayment",
                "description": "Send a Lightning Network payment",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymentrequest": {
                            "type": "string",
                            "description": "The Lightning Network payment request (invoice) to be paid. This encoded string contains all necessary details for the payment, including amount and recipient.",
                        },
                        "timeout": {
                            "type": "integer",
                            "description": "Maximum time (in seconds) allowed for finding a route for the payment. If a route isn't found within this time, the payment attempt will be aborted.",
                        },
                        "feelimit": {
                            "type": "integer",
                            "description": "Maximum fee (in millisatoshis) that the user is willing to pay for this transaction. If the calculated fee exceeds this limit, the payment will not be sent.",
                        }
                    },
                    "required": ["paymentrequest", "timeout", "feelimit"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "estimateroutefee",
                "description": "This endpoint calculates and returns an estimated fee for routing a Lightning Network payment based on the provided payment request. It helps users anticipate the cost of sending a payment before actually initiating the transaction.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymentrequest": {
                            "type": "string",
                            "description": "The Lightning Network payment request (invoice) for which the route fee is to be estimated. This encoded string contains necessary details such as the payment amount and recipient.",
                        },
                        "timeout": {
                            "type": "integer",
                            "description": "Maximum probing time (in seconds) allowed for finding a routing fee for the payment.",
                        }
                    },
                    "required": ["paymentrequest", "timeout"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "settleinvoice",
                "description": "Settles a previously accepted hold invoice using the provided preimage. This action finalizes the payment process for a hold invoice, releasing the funds to the invoice creator.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "preimage": {
                            "type": "string",
                            "description": "The preimage (32-byte hash preimage) that corresponds to the payment hash of the hold invoice to be settled. This preimage serves as proof of payment.",
                        }
                    },
                    "required": ["preimage"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "cancelinvoice",
                "description": "Cancels an open Lightning Network invoice. This endpoint allows users to cancel an invoice that hasn't been paid yet. If the invoice is already canceled, the operation succeeds. However, if the invoice has been settled, the cancellation will fail.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymenthash": {
                            "type": "string",
                            "description": "The payment hash of the invoice to be canceled. This unique identifier is used to locate the specific invoice in the system.",
                        }
                    },
                    "required": ["paymenthash"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getinvoice",
                "description": "Fetches and returns detailed information about a specific Lightning Network invoice identified by its payment hash. This endpoint allows users to access invoice details such as amount, status, and creation date.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymenthash": {
                            "type": "string",
                            "description": "The payment hash of the invoice to retrieve. This unique identifier is used to locate the specific invoice in the system.",
                        }
                    },
                    "required": ["paymenthash"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listinvoices",
                "description": "This endpoint returns a comprehensive list of all invoices associated with the authenticated user's account. It includes both paid and unpaid invoices, providing a complete overview of the account's invoice history.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "listpayments",
                "description": "This endpoint provides a list of all payments associated with the authenticated user's account that have not failed. This includes successful payments and those that are still in progress, offering a clear view of the account's payment activity.",
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getpayment",
                "description": "This endpoint fetches and returns detailed information about a specific payment identified by its payment hash. It provides comprehensive data about the payment, including its status, amount, and other relevant details.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymenthash": {
                            "type": "string",
                            "description": "Unique identifier (hash) of the payment to be retrieved. This hash is used to locate the specific payment record in the system.",
                        }
                    },
                    "required": ["paymenthash"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "getpayout",
                "description": "Get registered payouts.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "payoutId": {
                            "type": "string",
                            "description": "Payout id",
                        }
                    },
                    "required": ["payoutId"],
                },
            }
        },
        {
            "type": "function",
            "function": {
                "name": "cancelinvoicesendpayment",
                "description": "Cancels invoice and Initiates a Lightning Network payment atomically.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "paymenthash": {
                            "type": "string",
                            "description": "The Lightning Network payment hash of the invoice to be cancelled",
                        },
                        "paymentrequest": {
                            "type": "string",
                            "description": "The Lightning Network payment request (invoice) to be paid.",
                        },
                        "timeout": {
                            "type": "integer",
                            "description": "Maximum time (in seconds) allowed for finding a route for the payment.",
                        },
                        "feelimit": {
                            "type": "integer",
                            "description": "Maximum fee (in millisatoshis) that the user is willing to pay for this transaction.",
                        }
                    },
                    "required": ["paymenthash", "paymentrequest", "timeout", "feelimit"],
                },
            }
        }
    ]

    def __init__(self, privkey):
        self.theard = self.client.beta.threads.create()
        self.wallet = Wallet(os.environ["BASE_URL"], privkey)

    def prompt(self, prompt):
        self.messages.append({"role": "user", "content": prompt})

        response = self.client.chat.completions.create(
            model = self.GPT_MODEL,
            messages = self.messages,
            tools = self.tools
        )

        response_message = response.choices[0].message 
        self.messages.append(response_message)
        tool_calls = response_message.tool_calls

        if tool_calls:
            for tool_call in tool_calls: 
                tool_call_id = tool_call.id
                tool_function_name = tool_call.function.name
                tool_query_args = json.loads(tool_call.function.arguments)

                print(">", tool_call)

                if tool_function_name == 'topupandmine6blocks':
                    function_result = self.wallet.topupandmine6blocks(
                        bitcoinAddr = tool_query_args['bitcoinAddr'],
                        satoshis = tool_query_args['satoshis']
                    )
                elif tool_function_name == 'sendtoaddress':
                    function_result = self.wallet.sendtoaddress(
                        bitcoinAddr = tool_query_args['bitcoinAddr'],
                        satoshis = tool_query_args['satoshis']
                    )
                elif tool_function_name == 'generateblocks':
                    function_result = self.wallet.generateblocks(
                        blocknum = tool_query_args['blocknum']
                    )
                elif tool_function_name == 'newbitcoinaddress':
                    function_result = self.wallet.newbitcoinaddress()
                elif tool_function_name == 'getbitcoinwalletbalance':
                    function_result = self.wallet.getbitcoinwalletbalance(
                        minConf = tool_query_args['minConf']
                    )
                elif tool_function_name == 'getlndwalletbalance':
                    function_result = self.wallet.getlndwalletbalance()
                elif tool_function_name == 'openreserve':
                    function_result = self.wallet.openreserve(
                        satoshis = tool_query_args['satoshis']
                    )
                elif tool_function_name == 'closereserve':
                    function_result = self.wallet.closereserve(
                        reserveId = tool_query_args['reserveId']
                    )
                elif tool_function_name == 'listorphanedreserves':
                    function_result = self.wallet.listorphanedreserves()
                elif tool_function_name == 'listchannels':
                    function_result = self.wallet.listchannels(
                        activeOnly = tool_query_args['activeOnly']
                    )
                elif tool_function_name == 'listclosedchannels':
                    function_result = self.wallet.listclosedchannels()
                elif tool_function_name == 'estimatefee':
                    function_result = self.wallet.estimatefee(
                        address = tool_query_args['address'],
                        satoshis = tool_query_args['satoshis']
                    )
                elif tool_function_name == 'getbalance':
                    function_result = self.wallet.getbalance()
                elif tool_function_name == 'newaddress':
                    function_result = self.wallet.newaddress()
                elif tool_function_name == 'listtransactions':
                    function_result = self.wallet.listtransactions()
                elif tool_function_name == 'registerpayout':
                    function_result = self.wallet.registerpayout(
                        satoshis = tool_query_args['satoshis'], 
                        btcAddress = tool_query_args['btcAddress']
                    )
                elif tool_function_name == 'listpayouts':
                    function_result = self.wallet.listpayouts()
                elif tool_function_name == 'addinvoice':
                    function_result = self.wallet.addinvoice(
                        satoshis = tool_query_args['satoshis'], 
                        memo = tool_query_args['memo'], 
                        expiry = tool_query_args['expiry']
                    )
                elif tool_function_name == 'addhodlinvoice':
                    function_result = self.wallet.addhodlinvoice(
                        satoshis = tool_query_args['satoshis'],
                        hash = tool_query_args['hash'], 
                        memo = tool_query_args['memo'], 
                        expiry = tool_query_args['expiry']
                    )
                elif tool_function_name == 'decodeinvoice':
                    function_result = self.wallet.decodeinvoice(
                        paymentrequest = tool_query_args['paymentrequest']
                    )
                elif tool_function_name == 'sendpayment':
                    function_result = self.wallet.sendpayment(
                        paymentrequest = tool_query_args['paymentrequest'], 
                        timeout = tool_query_args['timeout'], 
                        feelimit = tool_query_args['feelimit']
                    )
                elif tool_function_name == 'estimateroutefee':
                    function_result = self.wallet.estimateroutefee(
                        paymentrequest = tool_query_args['paymentrequest'], 
                        timeout = tool_query_args['timeout']
                    )
                elif tool_function_name == 'settleinvoice':
                    function_result = self.wallet.settleinvoice(
                        preimage = tool_query_args['preimage']
                    )
                elif tool_function_name == 'cancelinvoice':
                    function_result = self.wallet.cancelinvoice(
                        paymenthash = tool_query_args['paymenthash']
                    )
                elif tool_function_name == 'getinvoice':
                    function_result = self.wallet.getinvoice(
                        paymenthash = tool_query_args['paymenthash']
                    )
                elif tool_function_name == 'listinvoices':
                    function_result = self.wallet.listinvoices()
                elif tool_function_name == 'listpayments':
                    function_result = self.wallet.listpayments()
                elif tool_function_name == 'getpayment':
                    function_result = self.wallet.getpayment(
                        paymenthash = tool_query_args['paymenthash']
                    )
                elif tool_function_name == 'getpayout':
                    function_result = self.wallet.getpayout(
                        payoutId = tool_query_args['payoutId']
                    )
                elif tool_function_name == 'cancelinvoicesendpayment':
                    function_result = self.wallet.cancelinvoicesendpayment(
                        paymenthash = tool_query_args['paymenthash'],
                        paymentrequest = tool_query_args['paymentrequest'],
                        timeout = tool_query_args['timeout'],
                        feelimit = tool_query_args['feelimit']
                    )
                else:
                    function_result = 'Unknown function.'

            print(">", function_result)

            self.messages.append({
                "role": "tool", 
                "tool_call_id": tool_call_id, 
                "name": tool_function_name, 
                "content": str(function_result)
            })
            
            response_with_function_call = self.client.chat.completions.create(
                model = self.GPT_MODEL,
                messages = self.messages
            )
            
            answer = response_with_function_call.choices[0].message.content
        else: 
            answer = response_message.content
                
        self.messages.append({"role": "assistant", "content": answer})
        return answer

def print_markdown(markdown_text):
    console = Console()
    lines = markdown_text.split('\n')
    table_lines = []
    in_table = False
    
    i = 0
    while i < len(lines):
        line = lines[i]
        
        # Detect table header separator (---|---|...)
        if line.strip().startswith('|') and i + 1 < len(lines) and all(c in '-|' for c in lines[i+1].strip()):
            in_table = True
            table_lines = [line]
            i += 1  # Skip the separator line
            
            # Collect all table rows
            while i + 1 < len(lines) and lines[i+1].strip().startswith('|'):
                i += 1
                table_lines.append(lines[i])
                
            # Process collected table
            table = Table()
            
            # Parse header
            headers = [cell.strip() for cell in table_lines[0].split('|')[1:-1]]
            for header in headers:
                table.add_column(header)
                
            # Parse rows
            for row in table_lines[2:]:
                cells = [cell.strip() for cell in row.split('|')[1:-1]]
                table.add_row(*cells)
                
            console.print(table)
            in_table = False
            
        elif not in_table:
            # Print non-table markdown
            console.print(Markdown(line))
            
        i += 1



def main():
    try:
        healthcheck = requests.get(url=f"{os.environ['BASE_URL']}/health")
        healthcheck.raise_for_status()

        print(colored(f"\nWelcome to the Cryptocurrency Wallet Management Console!\n", "cyan"))
        privkey = input(colored("Enter your private key: ", "cyan"))
        chat_agent = ChatAgent(privkey)

        while True:
            user_input = input(colored("\nPrompt:\n", "magenta"))
            
            if user_input.lower() in ['exit', 'quit']:
                print(colored("Exiting the chat. Goodbye!", "red"))
                break
            
            response = chat_agent.prompt(user_input)
            print(colored("\nAgent:", "green"))
            print_markdown(response)
            
    except Exception as e:
        print(colored(f"Could not connect to the server. Please make sure the server is running and accessible.\n{e}", "red"))
    

if __name__ == "__main__":
    main()







