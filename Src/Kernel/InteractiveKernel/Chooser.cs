using System;
using System.Threading.Tasks;
using Microsoft.Formula.CommandLine;
using Microsoft.Jupyter.Core.Protocol;

namespace Microsoft.Jupyter.Core
{
    public class Chooser : IChooser
    {
        private IShellServer _server;

        private Message cell_message;
        public Chooser(IShellServer server)
        {
            _server = server;
            Interactive = true;
        }

        public bool Interactive { get; set; }

        public bool GetChoice(out DigitChoiceKind choice)
        {
            RequestInputFromUser();
            var res = GetReplyOfInputFromClient();
            choice = (DigitChoiceKind)res;
            return true;
        }

        public void setCellMessage(Message msg)
        {
            cell_message = msg;
        }

        private void RequestInputFromUser()
        {
            var msg =new Message
            {
                ZmqIdentities = cell_message.ZmqIdentities,
                ParentHeader = cell_message.Header,
                Metadata = null,
                Content = new InputRequestContent
                {
                    Prompt = "Input selection: ",
                    Password = false
                },
                Header = new MessageHeader
                {
                    MessageType = "input_request",
                    Id = Guid.NewGuid().ToString()
                }
            };
            _server.SendStdinMessage(msg);
        }

        private int GetReplyOfInputFromClient()
        {
            Message msg = _server.ReceiveStdinMessage();
            if(msg != null
                && msg.Header.MessageType == "input_reply")
            {
                return Int32.Parse(((InputReplyContent)msg.Content).Value);
            }
            return 0;
        }
    }
}
