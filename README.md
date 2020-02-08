# MockChannel
This mock channel was cloned from the [MockChannel](https://github.com/Microsoft/BotFramework-Samples/tree/master/blog-samples/CSharp/MockChannel)
sample provided in the [BotFramework-Samples repo](https://github.com/microsoft/BotFramework-Samples).

When creating load tests, you need a mock channel service to pass as activity.ServiceURL which 
the bot framework will then use to POST back it's response as described in [Load testing a Bot](https://blog.botframework.com/2017/06/19/Load-Testing-A-Bot/) 
blog post. 

To use:
* Configure all 3 configuration files: 
  * Web.config - in project root folder
  * appsettings.json - in EchoBot project folder
  * app.config - in WebAndLoadTestProject proect folder

When you've configured the solution start both the EchoBot and MockChannel proejects and
then double click the LoadTest1.loadtest to launch the load test designer and run
the load test.  You can fiddle with the load test to your likings.

Here are a set of PostMan request that will help you with the load test
