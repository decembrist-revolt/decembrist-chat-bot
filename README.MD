FastReply
=========
The feature allows you to configure automatic replies to specific messages using either text or stickers.

To set up the plugin, edit the FastReply section in the appsettings.json file.

**Key**: The trigger text that initiates the automatic reply.

**Value**: The content of the automatic reply.

Usage:

Enter plain text as value to reply with a text message.

Enter `$sticker_yourFileId` as value to reply with a sticker.

Commands
--------
1. Any sticker - Obtain sticker's fileId
2. /me - View your own telegram id.
3. /status - View the bot's status.
4. Send a sticker fileId in format `$sticker_yourFileId` to show the sticker